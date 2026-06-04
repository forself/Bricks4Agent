using System.Net.Http.Json;
using System.Text.Json;

namespace Broker.Services;

/// <summary>
/// Discord 通知推播（HostedService）
///
/// 運作方式：
///   - 啟動時讀 appsettings `Notifications:Discord:WebhookUrl`（或環境變數 Notifications__Discord__WebhookUrl）
///   - URL 為空 → 服務直接 disabled，什麼都不做（graceful no-op）
///   - URL 存在 → 每 N 秒 poll `PriceAlertService.History` + `AutoTraderService.RecentLogs`
///   - 偵測到新事件 → POST Discord embed 到 webhook
///
/// 零侵入設計：
///   - **不改** PriceAlertService 和 AutoTraderService
///   - 直接注入它們（都已是 singleton），讀 public 屬性即可
///   - 啟動時先吸收「既有 history」到 seen set，避免開機瞬間爆訊息
///
/// 推的事件：
///   - 價格告警觸發（from PriceAlertService.History）
///   - Auto-trader 下單成功 / 失敗（from AutoTraderService.RecentLogs action=buy/sell/error）
/// </summary>
public class DiscordNotificationService : BackgroundService
{
    private readonly PriceAlertService _alerts;
    private readonly AutoTraderService _autoTrader;
    private readonly IHttpClientFactory _httpFactory;
    private readonly NotificationDedupRepo _dedup;
    private readonly ILogger<DiscordNotificationService> _logger;
    private readonly string _webhookUrl;
    // 2026-06-04:DM 模式 — 設了 bot token + 你的 user ID → operator 通知改 DM 給你(只有你收得到、不進任何頻道)。
    private readonly string _botToken;
    private readonly string _dmUserId;
    private readonly int _intervalSeconds;

    private readonly HashSet<string> _seenAlertKeys = new();
    private readonly HashSet<string> _seenLogKeys = new();

    // ── Heartbeat watchdog（#2）──
    // auto-trader 開了卻沒在跑（broker process freeze / dispatcher hang）→ 要叫醒人。
    // 觀察方式：每次 cycle Discord poll 時檢查 LastCycleAt 是否超過 _heartbeatStaleMinutes。
    // 第一次偵測到 stale 才推 1 次警報，避免每 15 秒 spam 同一封警告。
    private bool _heartbeatStaleNotified = false;
    private readonly int _heartbeatStaleMinutes;

    // ── protect 洗版控制 ──
    // action="protect" 被重用:SL 移動(SL→BE/Trailing)+ exchange SL set 確認 = 高頻噪音;
    // 但實際保護平倉(perp close…)= 重要、要留。ProtectNotify=false 連 SL 移動也不推。
    private readonly bool _protectNotify;
    private readonly TimeSpan _protectThrottle;

    public DiscordNotificationService(
        PriceAlertService alerts,
        AutoTraderService autoTrader,
        IHttpClientFactory httpFactory,
        NotificationDedupRepo dedup,
        IConfiguration config,
        ILogger<DiscordNotificationService> logger)
    {
        _alerts = alerts;
        _autoTrader = autoTrader;
        _httpFactory = httpFactory;
        _dedup = dedup;
        _logger = logger;
        _webhookUrl = config["Notifications:Discord:WebhookUrl"] ?? "";
        _botToken = config["Notifications:Discord:BotToken"] ?? "";
        _dmUserId = config["Notifications:Discord:DmUserId"] ?? "";
        _intervalSeconds = Math.Max(10, config.GetValue("Notifications:Discord:IntervalSeconds", 15));
        // Auto-trader cycle 預設 5 分鐘、給寬限到 15 分鐘才視為 stale
        _heartbeatStaleMinutes = Math.Max(2, config.GetValue("Notifications:Discord:HeartbeatStaleMinutes", 15));
        // protect 通知:SL 移動類每 symbol 最多每 N 分鐘推一則;false=完全不推 SL 移動(實際平倉仍推)
        _protectNotify = config.GetValue("Notifications:Discord:ProtectNotify", true);
        _protectThrottle = TimeSpan.FromMinutes(Math.Max(1, config.GetValue("Notifications:Discord:ProtectThrottleMinutes", 60)));
    }

    // DM 模式優先:設了 bot token + user ID 就走 DM;否則看 webhook。
    private bool DmEnabled => !string.IsNullOrWhiteSpace(_botToken) && !string.IsNullOrWhiteSpace(_dmUserId);
    public bool IsEnabled => DmEnabled || !string.IsNullOrWhiteSpace(_webhookUrl);
    public string MaskedWebhook => IsEnabled
        ? _webhookUrl[..Math.Min(40, _webhookUrl.Length)] + "…"
        : "";
    public int IntervalSeconds => _intervalSeconds;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!IsEnabled)
        {
            _logger.LogInformation(
                "Discord notifications DISABLED (set Notifications__Discord__WebhookUrl to enable)");
            return;
        }

        _logger.LogInformation(
            "Discord notifications ENABLED, polling every {S}s", _intervalSeconds);

        // 吸收啟動時已存在的 history → 不要重送舊訊息
        foreach (var e in _alerts.History) _seenAlertKeys.Add(AlertKey(e));
        foreach (var l in _autoTrader.RecentLogs) _seenLogKeys.Add(LogKey(l));

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await CheckAlertsAsync(ct);
                await CheckTradeLogsAsync(ct);
                await CheckHeartbeatAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Discord poll error");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    // ── 檢查新事件 ───────────────────────────────────────────────────

    private async Task CheckAlertsAsync(CancellationToken ct)
    {
        foreach (var e in _alerts.History)
        {
            var key = AlertKey(e);
            if (_seenAlertKeys.Contains(key)) continue;
            _seenAlertKeys.Add(key);

            var up = e.Condition.Equals("above", StringComparison.OrdinalIgnoreCase);
            var arrow = up ? "📈" : "📉";
            var color = up ? 0x0ECB81 : 0xF6465D;  // green / red

            var fields = string.IsNullOrEmpty(e.Note)
                ? null
                : new[] { new { name = "備註", value = e.Note, inline = false } };

            await SendEmbedAsync(
                title:       $"{arrow} 價格告警觸發 · {e.Symbol}",
                description: $"**{e.Symbol}** 目前 `${e.CurrentPrice:N4}` "
                           + (up ? "突破上方門檻" : "跌破下方門檻")
                           + $" `${e.TargetPrice:N4}`",
                color:       color,
                fields:      fields,
                timestamp:   e.TriggeredAt,
                ct:          ct);
        }
    }

    // 5/19 改：dedup state 從 in-memory Dictionary 搬到 NotificationDedupRepo（持久化）。
    // 之前 broker rebuild 後 in-memory 字典清空、同樣訊息每次重啟都推一次（一天 rebuild 7+
    // 次 = 用戶收到 9 條 spam）。現在 SQLite 表記得跨重啟。
    private static readonly TimeSpan ErrorDedupWindow = TimeSpan.FromMinutes(30);

    // paper 實驗場交易所:不推真錢告警頻道(避免淹沒真錢訊號 + 保護單 422 噪音);paper 看 dashboard。
    // env NOTIFY_SUPPRESS_EXCHANGES 可覆寫(要在 compose 接線才吃 env);預設 alpaca,binance。
    private static readonly HashSet<string> PaperExchanges =
        (Environment.GetEnvironmentVariable("NOTIFY_SUPPRESS_EXCHANGES") ?? "alpaca,binance")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(s => s.ToLowerInvariant()).ToHashSet();

    private static bool IsErrorAction(string action) =>
        action == "error" || action == "blocked" || action == "halt" || action.Contains("fail");

    private async Task CheckTradeLogsAsync(CancellationToken ct)
    {
        foreach (var l in _autoTrader.RecentLogs)
        {
            var key = LogKey(l);
            if (_seenLogKeys.Contains(key)) continue;
            _seenLogKeys.Add(key);

            // paper 實驗場(alpaca/binance)不推真錢頻道 —— 真錢頻道只留 bingx,paper 看 dashboard
            if (PaperExchanges.Contains((l.Exchange ?? "").ToLowerInvariant())) continue;

            // 只推「會導致部位變動 / 需要注意」的事件，一般 skip/hold/dedup 不推，避免訊息爆量
            var action = l.Action.ToLowerInvariant();
            var (emoji, color, prefix) = ClassifyAction(action);
            if (emoji == "") continue;

            // ── protect 洗版控制 ──
            // SL→BE / Trailing lock = 每次 ratchet 都來、洗版主因 → 每 symbol 節流(number-stripped 不適用、
            //   直接用 symbol 當鍵 → 同倉一窗內只推一則);exchange SL set = 純確認回執 → 永不推;
            //   實際保護平倉(perp close… / "{SIDE} {qty} — …")= 真的動部位、重要 → 不攔、照常推。
            if (action == "protect")
            {
                var pmsg = l.Message ?? "";
                if (pmsg.Contains("exchange SL set", StringComparison.OrdinalIgnoreCase))
                    continue;  // 確認回執、不推
                var isSlMove = pmsg.Contains("Trailing lock", StringComparison.OrdinalIgnoreCase)
                            || pmsg.Contains("SL → BE", StringComparison.OrdinalIgnoreCase)
                            || pmsg.Contains("SL -> BE", StringComparison.OrdinalIgnoreCase);
                if (isSlMove)
                {
                    if (!_protectNotify) continue;                 // 完全關掉 SL 移動推播
                    var psig = $"protect-slmove|{l.Symbol}";
                    if (_dedup.IsRecentlySent("discord", psig, _protectThrottle)) continue;
                    _dedup.MarkSent("discord", psig);
                }
                // 其餘 protect(實際保護平倉/下單)落到下面照常推
            }

            // 錯誤類 dedup：同 signature 30 分鐘內已推過就略過（5/19 持久化）。
            // ⚠ 簽章必須抽掉訊息裡的數字 —— 否則像「Net perp notional 1039 …」的 1039 每輪隨行情變動
            // → 簽章每次不同 → 去重失效 → 同一則失敗每 5 分鐘洗版一次（5/24 修)。
            if (IsErrorAction(action))
            {
                var msgStable = System.Text.RegularExpressions.Regex.Replace(l.Message ?? "", @"[\d.]+", "#");
                var msgPrefix = msgStable.Length > 60 ? msgStable[..60] : msgStable;
                var sig = $"{action}|{l.Symbol}|{msgPrefix}";
                if (_dedup.IsRecentlySent("discord", sig, ErrorDedupWindow))
                    continue;
                _dedup.MarkSent("discord", sig);
            }

            var fields = new[]
            {
                new { name = "Symbol",   value = l.Symbol,        inline = true },
                new { name = "Exchange", value = l.Exchange ?? "", inline = true },
                new { name = "Action",   value = action,          inline = true },
            };

            await SendEmbedAsync(
                title:       $"{emoji} {prefix} · {l.Symbol}",
                description: l.Message ?? "",
                color:       color,
                fields:      fields,
                timestamp:   l.Time,
                ct:          ct);
        }
        // 防 seen-set 無限成長:剪到只剩當前 RecentLogs 視窗(≤200)內的 key
        //（舊 log 已從 _tradeLog 上限 dequeue、不會再出現,留在 seen 裡只是慢性洩漏）
        _seenLogKeys.IntersectWith(_autoTrader.RecentLogs.Select(LogKey));
    }

    // ── 對外 API（供 Endpoint 呼叫）───────────────────────────────────

    /// <summary>
    /// 推一則任意 embed（給 DailyReportService 等內部服務組好文字後呼叫）。
    /// 不走 dedup / RecentLogs、直接送、由 caller 控制節流。
    /// </summary>
    public async Task<(bool ok, string? error)> SendAdHocAsync(
        string title, string body, int color = 0x2B6CB0, CancellationToken ct = default)
    {
        if (!IsEnabled) return (false, "Webhook URL not configured");
        try
        {
            await SendEmbedAsync(title, body, color, fields: null, timestamp: DateTime.UtcNow, ct: ct);
            return (true, null);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    /// <summary>
    /// 多用戶:推一則 embed 到「指定」webhook（不是全域那個）。給每個朋友自己的頻道用。
    /// 不走 dedup、由 caller 控制節流。
    /// </summary>
    public async Task<(bool ok, string? error)> SendAdHocToWebhookAsync(
        string webhookUrl, string title, string body, int color = 0x2B6CB0, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(webhookUrl)) return (false, "Empty webhook URL");
        try
        {
            await SendEmbedToUrlAsync(webhookUrl, title, body, color, fields: null, timestamp: DateTime.UtcNow, ct: ct);
            return (true, null);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public async Task<(bool ok, string? error)> SendTestAsync(string? customMessage = null)
    {
        if (!IsEnabled) return (false, "Webhook URL not configured");
        try
        {
            await SendEmbedAsync(
                title:       "🧪 測試訊息",
                description: string.IsNullOrWhiteSpace(customMessage)
                    ? "B4A Broker → Discord 推播管線運作正常。"
                    : customMessage!,
                color:       0x2B6CB0,
                fields:      new[]
                {
                    new { name = "Broker",   value = Environment.MachineName, inline = true },
                    new { name = "Interval", value = $"{_intervalSeconds}s", inline = true },
                },
                timestamp:   DateTime.UtcNow,
                ct:          CancellationToken.None);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // ── #2 Heartbeat watchdog ──────────────────────────────────────
    // 偵測 auto-trader 開了卻沒在 cycle（broker freeze / 主迴圈卡死）。
    // 第一次偵測到 stale 推 1 次警報；恢復正常時推「已恢復」訊息並 reset 旗。
    private async Task CheckHeartbeatAsync(CancellationToken ct)
    {
        // 沒 enabled 就不檢查——關著的 auto-trader 永遠不該 heartbeat
        if (!_autoTrader.IsEnabled)
        {
            // 從 stale 變成 disabled 時也清旗，避免使用者重啟後立刻誤報
            _heartbeatStaleNotified = false;
            return;
        }

        var lastCycle = _autoTrader.LastCycleAt;
        var now = DateTime.UtcNow;

        // 從 enable 到第一次 cycle 之間有個 gap（最多 _intervalSeconds），給 2x 寬限
        var gracePeriod = TimeSpan.FromSeconds(Math.Max(60, _autoTrader.IntervalSeconds * 2));

        bool isStale;
        TimeSpan? sinceCycle = lastCycle is { } at ? now - at : null;
        if (sinceCycle == null)
        {
            // 從未 cycle 過——只有在啟用後超過 grace 才視為 stale
            // 但這邊拿不到「何時 enable」、保守一點不警報、直接交給人為觀察
            isStale = false;
        }
        else
        {
            isStale = sinceCycle.Value.TotalMinutes >= _heartbeatStaleMinutes;
        }

        if (isStale && !_heartbeatStaleNotified)
        {
            _heartbeatStaleNotified = true;
            _logger.LogWarning(
                "⚠ Auto-trader heartbeat STALE: last cycle {SinceMin:F1} min ago (≥ {ThresholdMin} min)",
                sinceCycle?.TotalMinutes ?? -1, _heartbeatStaleMinutes);
            await SendEmbedAsync(
                title:       "⚠️ Auto-Trader Heartbeat Stale",
                description: $"自動交易啟用中、但已 **{sinceCycle?.TotalMinutes:F1}** 分鐘沒跑 cycle "
                           + $"（門檻 {_heartbeatStaleMinutes} 分）。可能 broker 卡死、worker 失聯、"
                           + $"或 dispatcher 死鎖。請檢查 broker 容器 + worker 狀態。",
                color:       0xFCD535,
                fields:      new[]
                {
                    new { name = "Last Cycle",    value = lastCycle?.ToString("u") ?? "never", inline = true },
                    new { name = "Watch Count",   value = _autoTrader.WatchList.Count.ToString(), inline = true },
                    new { name = "Interval",      value = $"{_autoTrader.IntervalSeconds}s", inline = true },
                },
                timestamp:   now,
                ct:          ct);
        }
        else if (!isStale && _heartbeatStaleNotified)
        {
            // 恢復正常——推「已恢復」訊息並 clear 旗
            _heartbeatStaleNotified = false;
            _logger.LogInformation("✓ Auto-trader heartbeat recovered");
            await SendEmbedAsync(
                title:       "✅ Auto-Trader Heartbeat 恢復",
                description: $"Cycle 已重新跑起來、距上次 cycle {sinceCycle?.TotalSeconds:F0} 秒。",
                color:       0x0ECB81,
                fields:      null,
                timestamp:   now,
                ct:          ct);
        }
    }

    // 把 AutoTrader 多樣 action key 歸類成 (emoji, color, 中文 prefix)。
    // perp 端 (open_long / close_short / scale_in_* / protect / halt) 跟 spot 端 (buy/sell) 都要涵蓋；
    // hold / skip / dedup / warn / force 之類噪音事件不推（emoji 留空 → caller continue）。
    private static (string Emoji, int Color, string Prefix) ClassifyAction(string action)
    {
        // 變體前綴：scale_in_long_xxx 之類用 StartsWith 抓
        if (action.StartsWith("scale_in_long"))  return ("➕", 0x3B82F6, "Auto-Trader 加碼多");
        if (action.StartsWith("scale_in_short")) return ("➕", 0x3B82F6, "Auto-Trader 加碼空");

        return action switch
        {
            "buy"         => ("🟢", 0x0ECB81, "Auto-Trader 買入"),
            "sell"        => ("🔴", 0xF6465D, "Auto-Trader 賣出"),
            "open_long"   => ("🟢", 0x0ECB81, "Auto-Trader 開多"),
            "open_short"  => ("🔴", 0xF6465D, "Auto-Trader 開空"),
            "close_long"  => ("🟡", 0xFCD535, "Auto-Trader 平多"),
            "close_short" => ("🟡", 0xFCD535, "Auto-Trader 平空"),
            "protect"     => ("🛡", 0xFCD535, "Auto-Trader 保護單"),
            "halt"        => ("⛔", 0xF6465D, "Auto-Trader 熔斷"),
            "error"       => ("⚠️", 0xFCD535, "Auto-Trader 錯誤"),
            "blocked"     => ("🛑", 0xF6465D, "Auto-Trader 被風控擋下"),
            "adjusted"    => ("✂️", 0xFCD535, "Auto-Trader 數量調整"),
            _             => ("", 0, ""),
        };
    }

    // ── 底層 webhook ─────────────────────────────────────────────────

    private Task SendEmbedAsync(
        string title,
        string description,
        int color,
        object? fields,
        DateTime? timestamp,
        CancellationToken ct)
        => DmEnabled
            ? SendEmbedViaDmAsync(title, description, color, fields, timestamp, ct)   // 2026-06-04:operator 通知改 DM 給你(只有你看到)
            : SendEmbedToUrlAsync(_webhookUrl, title, description, color, fields, timestamp, ct);

    // 2026-06-04:DM operator 通知 — 用 bot token(複用 bot-node 的)開跟你的 DM channel + 發 embed。
    // 只有你收得到、不進任何頻道(解共享頻道洩漏策略名/動作)。bot 需跟你同 server + 你允許 server 成員 DM。
    private async Task SendEmbedViaDmAsync(
        string title, string description, int color, object? fields, DateTime? timestamp, CancellationToken ct)
    {
        var embed = new
        {
            title, description, color, fields,
            timestamp = timestamp?.ToUniversalTime().ToString("o"),
            footer = new { text = "B4A Trading · Broker notification (DM)" },
        };
        using var client = _httpFactory.CreateClient("discord-webhook");
        client.Timeout = TimeSpan.FromSeconds(10);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bot {_botToken}");
        try
        {
            // 1. 開(或取既有)跟該 user 的 DM channel
            var dmResp = await client.PostAsJsonAsync(
                "https://discord.com/api/v10/users/@me/channels", new { recipient_id = _dmUserId }, ct);
            if (!dmResp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Discord DM channel create {Status}: {Body}",
                    dmResp.StatusCode, await dmResp.Content.ReadAsStringAsync(ct));
                return;
            }
            var dm = await dmResp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            if (dm.ValueKind != JsonValueKind.Object || !dm.TryGetProperty("id", out var idEl))
            {
                _logger.LogWarning("Discord DM channel: no id in response");
                return;
            }
            // 2. 發 embed 到該 DM channel
            var msgResp = await client.PostAsJsonAsync(
                $"https://discord.com/api/v10/channels/{idEl.GetString()}/messages",
                new { embeds = new[] { embed } }, ct);
            if (!msgResp.IsSuccessStatusCode)
                _logger.LogWarning("Discord DM send {Status}: {Body}",
                    msgResp.StatusCode, await msgResp.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Discord DM POST failed"); }
    }

    private async Task SendEmbedToUrlAsync(
        string webhookUrl,
        string title,
        string description,
        int color,
        object? fields,
        DateTime? timestamp,
        CancellationToken ct)
    {
        var embed = new
        {
            title,
            description,
            color,
            fields,
            timestamp = timestamp?.ToUniversalTime().ToString("o"),
            footer = new { text = "B4A Trading · Broker notification" },
        };

        var payload = new
        {
            username = "B4A Broker",
            embeds = new[] { embed },
        };

        using var client = _httpFactory.CreateClient("discord-webhook");
        client.Timeout = TimeSpan.FromSeconds(10);

        try
        {
            var resp = await client.PostAsJsonAsync(webhookUrl, payload, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Discord webhook {Status}: {Body}", resp.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Discord webhook POST failed");
        }
    }

    // ── 去重 key 產生 ────────────────────────────────────────────────

    private static string AlertKey(AlertEvent e)
        => $"{e.Id}@{e.TriggeredAt.Ticks}";

    private static string LogKey(TradeLog l)
        => $"{l.Symbol}|{l.Exchange}|{l.Time.Ticks}|{l.Action}";
}
