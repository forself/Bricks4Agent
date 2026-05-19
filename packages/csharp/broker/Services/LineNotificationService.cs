using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Services;
using FunctionPool.Registry;

namespace Broker.Services;

/// <summary>
/// LINE 通知推播（HostedService）——跟 DiscordNotificationService 完全平行的設計、
/// 同一份事件來源（PriceAlertService.History + AutoTraderService.RecentLogs +
/// AutoTrader heartbeat）、只是出口換成 line.notification.send capability。
///
/// 啟用條件（兩個都要）：
///   - config `Notifications:Line:Enabled` = true
///     （預設 false——避免你只設了 line-worker token 卻還沒準備好收推播時就誤發）
///   - line-worker 線上（capability `line.notification.send` 可用）
/// 任一條件不滿足 → 服務啟動但所有 send 都 skip，broker 不會因此死掉。
///
/// recipient 優先序：
///   - config `Notifications:Line:RecipientId`（broker 端覆蓋）
///   - line-worker 端 `Line:DefaultRecipientId`（worker 自己的預設）
///     ——broker 不傳 to、worker 會用自己的 default
///
/// 跟 Discord 行為完全對齊：同樣的 dedup key、同樣的事件 filter（buy/sell/error/blocked/
/// adjusted）、同樣的 heartbeat watchdog 邏輯。差別僅在訊息格式：LINE 沒有 embed、
/// 改用「icon + 標題 + 純文字 body」（line.notification.send handler 自己會加 emoji prefix）。
/// </summary>
public class LineNotificationService : BackgroundService
{
    private readonly PriceAlertService _alerts;
    private readonly AutoTraderService _autoTrader;
    private readonly IExecutionDispatcher _dispatcher;
    private readonly IWorkerRegistry _registry;
    private readonly ILogger<LineNotificationService> _logger;

    private readonly bool _enabledInConfig;
    private readonly string? _recipientOverride;
    private readonly int _intervalSeconds;
    private readonly int _heartbeatStaleMinutes;

    private readonly HashSet<string> _seenAlertKeys = new();
    private readonly HashSet<string> _seenLogKeys = new();
    private bool _heartbeatStaleNotified;
    private readonly NotificationDedupRepo _dedup;

    public LineNotificationService(
        PriceAlertService alerts,
        AutoTraderService autoTrader,
        IExecutionDispatcher dispatcher,
        IWorkerRegistry registry,
        NotificationDedupRepo dedup,
        IConfiguration config,
        ILogger<LineNotificationService> logger)
    {
        _alerts = alerts;
        _autoTrader = autoTrader;
        _dispatcher = dispatcher;
        _registry = registry;
        _dedup = dedup;
        _logger = logger;
        _enabledInConfig = config.GetValue("Notifications:Line:Enabled", false);
        _recipientOverride = config["Notifications:Line:RecipientId"];
        _intervalSeconds = Math.Max(10, config.GetValue("Notifications:Line:IntervalSeconds", 15));
        _heartbeatStaleMinutes = Math.Max(2, config.GetValue("Notifications:Line:HeartbeatStaleMinutes", 15));
    }

    public bool IsEnabledInConfig => _enabledInConfig;
    public bool IsWorkerAvailable => _registry.HasAvailableWorker("line.notification.send");
    public bool IsActive => _enabledInConfig && IsWorkerAvailable;
    public string MaskedRecipient => string.IsNullOrEmpty(_recipientOverride)
        ? "(default from worker)"
        : _recipientOverride[..Math.Min(8, _recipientOverride.Length)] + "...";
    public int IntervalSeconds => _intervalSeconds;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_enabledInConfig)
        {
            _logger.LogInformation(
                "LINE notifications DISABLED (set Notifications__Line__Enabled=true to enable)");
            return;
        }

        _logger.LogInformation(
            "LINE notifications ENABLED, polling every {S}s (recipient={R})",
            _intervalSeconds, MaskedRecipient);

        // 啟動時吸收既有 history → 不重送舊訊息
        foreach (var e in _alerts.History) _seenAlertKeys.Add(AlertKey(e));
        foreach (var l in _autoTrader.RecentLogs) _seenLogKeys.Add(LogKey(l));

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (IsWorkerAvailable)
                {
                    await CheckAlertsAsync(ct);
                    await CheckTradeLogsAsync(ct);
                    await CheckHeartbeatAsync(ct);
                }
                // worker 不在線就靜默 skip——別 spam log，broker 早晚會 reconnect
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LINE poll error");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    // ── 事件檢查（dedup 用記憶體 set、跟 Discord 路徑各自獨立）──

    private async Task CheckAlertsAsync(CancellationToken ct)
    {
        foreach (var e in _alerts.History)
        {
            var key = AlertKey(e);
            if (_seenAlertKeys.Contains(key)) continue;
            _seenAlertKeys.Add(key);

            var up = e.Condition.Equals("above", StringComparison.OrdinalIgnoreCase);
            var arrow = up ? "📈" : "📉";
            var title = $"{arrow} 價格告警 · {e.Symbol}";
            var body = $"{e.Symbol} 目前 ${e.CurrentPrice:N4}\n"
                     + (up ? $"突破上方門檻 ${e.TargetPrice:N4}" : $"跌破下方門檻 ${e.TargetPrice:N4}");
            if (!string.IsNullOrEmpty(e.Note)) body += $"\n備註：{e.Note}";

            await SendNotificationAsync(title, body, level: up ? "info" : "warning", ct);
        }
    }

    // 錯誤訊息 dedup（跟 Discord 同樣機制、30 分鐘 cooldown）
    // 5/19 改：搬到 NotificationDedupRepo 持久化、broker rebuild 不再清空 dedup state
    private static readonly TimeSpan ErrorDedupWindow = TimeSpan.FromMinutes(30);

    private static bool IsErrorAction(string action) =>
        action == "error" || action == "blocked" || action == "halt" || action.Contains("fail");

    private async Task CheckTradeLogsAsync(CancellationToken ct)
    {
        foreach (var l in _autoTrader.RecentLogs)
        {
            var key = LogKey(l);
            if (_seenLogKeys.Contains(key)) continue;
            _seenLogKeys.Add(key);

            var action = l.Action.ToLowerInvariant();
            var (prefix, level) = ClassifyAction(action);
            if (string.IsNullOrEmpty(prefix)) continue;

            // 錯誤類 dedup：30 分鐘內相同 signature 不重推
            if (IsErrorAction(action))
            {
                var msgPrefix = l.Message?.Length > 60 ? l.Message[..60] : (l.Message ?? "");
                var sig = $"{action}|{l.Symbol}|{msgPrefix}";
                if (_dedup.IsRecentlySent("line", sig, ErrorDedupWindow))
                    continue;
                _dedup.MarkSent("line", sig);
            }

            var title = $"{prefix} · {l.Symbol}";
            var body = $"{l.Symbol} @ {l.Exchange}\n動作：{action}\n{l.Message}";
            await SendNotificationAsync(title, body, level, ct);
        }
    }

    // 跟 DiscordNotificationService.ClassifyAction 對齊（無 emoji/color、回 (中文標題, level)）。
    // perp action key 變體用 StartsWith；scale_in / protect / halt 都會推。
    private static (string Prefix, string Level) ClassifyAction(string action)
    {
        if (action.StartsWith("scale_in_long"))  return ("Auto-Trader 加碼多", "info");
        if (action.StartsWith("scale_in_short")) return ("Auto-Trader 加碼空", "info");

        return action switch
        {
            "buy"         => ("Auto-Trader 買入",      "success"),
            "sell"        => ("Auto-Trader 賣出",      "info"),
            "open_long"   => ("Auto-Trader 開多",      "success"),
            "open_short"  => ("Auto-Trader 開空",      "info"),
            "close_long"  => ("Auto-Trader 平多",      "warning"),
            "close_short" => ("Auto-Trader 平空",      "warning"),
            "protect"     => ("Auto-Trader 保護單",    "warning"),
            "halt"        => ("Auto-Trader 熔斷",      "error"),
            "error"       => ("Auto-Trader 錯誤",      "error"),
            "blocked"     => ("Auto-Trader 被風控擋下", "warning"),
            "adjusted"    => ("Auto-Trader 數量調整",  "warning"),
            _             => ("", ""),
        };
    }

    private async Task CheckHeartbeatAsync(CancellationToken ct)
    {
        if (!_autoTrader.IsEnabled)
        {
            _heartbeatStaleNotified = false;
            return;
        }

        var lastCycle = _autoTrader.LastCycleAt;
        var now = DateTime.UtcNow;
        TimeSpan? sinceCycle = lastCycle is { } at ? now - at : null;
        bool isStale = sinceCycle?.TotalMinutes >= _heartbeatStaleMinutes;

        if (isStale && !_heartbeatStaleNotified)
        {
            _heartbeatStaleNotified = true;
            await SendNotificationAsync(
                title: "Auto-Trader Heartbeat Stale",
                body: $"自動交易啟用中、但已 {sinceCycle?.TotalMinutes:F1} 分鐘沒跑 cycle "
                    + $"（門檻 {_heartbeatStaleMinutes} 分）。\n"
                    + $"上次 cycle：{lastCycle?.ToString("u") ?? "never"}\n"
                    + $"監控數：{_autoTrader.WatchList.Count}\n"
                    + $"請檢查 broker / worker 狀態。",
                level: "warning",
                ct: ct);
        }
        else if (!isStale && _heartbeatStaleNotified)
        {
            _heartbeatStaleNotified = false;
            await SendNotificationAsync(
                title: "Auto-Trader Heartbeat 恢復",
                body: $"Cycle 已重新跑起來（距上次 cycle {sinceCycle?.TotalSeconds:F0} 秒）。",
                level: "success",
                ct: ct);
        }
    }

    // ── 對外（給 NotificationEndpoints 的 /test 用）──

    /// <summary>
    /// 推一則任意通知（給 DailyReportService 等內部服務用）。
    /// 不過 dedup、由 caller 控制節流。
    /// </summary>
    public async Task<(bool ok, string? error)> SendAdHocAsync(
        string title, string body, string level = "info", CancellationToken ct = default)
    {
        if (!_enabledInConfig) return (false, "LINE notifications disabled in config");
        try
        {
            await SendNotificationAsync(title, body, level, ct);
            return (true, null);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public async Task<(bool ok, string? error)> SendTestAsync(string? customMessage = null)
    {
        if (!_enabledInConfig) return (false, "LINE notifications disabled in config");
        if (!IsWorkerAvailable) return (false, "line-worker not connected");
        var body = string.IsNullOrWhiteSpace(customMessage)
            ? "B4A Broker → LINE 推播管線運作正常。"
            : customMessage!;
        await SendNotificationAsync("測試訊息", body, "info", CancellationToken.None);
        return (true, null);
    }

    // ── 底層：dispatch line.notification.send capability ──

    private async Task SendNotificationAsync(string title, string body, string level, CancellationToken ct)
    {
        try
        {
            var args = new Dictionary<string, object?>
            {
                ["title"] = title,
                ["body"] = body,
                ["level"] = level,
            };
            if (!string.IsNullOrEmpty(_recipientOverride))
                args["to"] = _recipientOverride;

            var payload = JsonSerializer.Serialize(new { args });
            var req = new ApprovedRequest
            {
                RequestId = Guid.NewGuid().ToString("N"),
                CapabilityId = "line.notification.send",
                Route = "send",
                Payload = payload,
                Scope = "{}", PrincipalId = "system",
                TaskId = "line-notification", SessionId = "line-notification",
            };
            var result = await _dispatcher.DispatchAsync(req);
            if (!result.Success)
                _logger.LogWarning("LINE dispatch failed: {Err}", result.ErrorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LINE notification send failed");
        }
    }

    // ── 去重 key（跟 Discord 各自獨立、避免 race）──
    private static string AlertKey(AlertEvent e) => $"{e.Id}@{e.TriggeredAt.Ticks}";
    private static string LogKey(TradeLog l) => $"{l.Symbol}|{l.Exchange}|{l.Time.Ticks}|{l.Action}";
}
