using System.Net.Http.Json;

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
    private readonly ILogger<DiscordNotificationService> _logger;
    private readonly string _webhookUrl;
    private readonly int _intervalSeconds;

    private readonly HashSet<string> _seenAlertKeys = new();
    private readonly HashSet<string> _seenLogKeys = new();

    public DiscordNotificationService(
        PriceAlertService alerts,
        AutoTraderService autoTrader,
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILogger<DiscordNotificationService> logger)
    {
        _alerts = alerts;
        _autoTrader = autoTrader;
        _httpFactory = httpFactory;
        _logger = logger;
        _webhookUrl = config["Notifications:Discord:WebhookUrl"] ?? "";
        _intervalSeconds = Math.Max(10, config.GetValue("Notifications:Discord:IntervalSeconds", 15));
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_webhookUrl);
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

    private async Task CheckTradeLogsAsync(CancellationToken ct)
    {
        foreach (var l in _autoTrader.RecentLogs)
        {
            var key = LogKey(l);
            if (_seenLogKeys.Contains(key)) continue;
            _seenLogKeys.Add(key);

            // 只推「會導致部位變動 / 需要注意」的事件，一般 skip/hold 不推，避免訊息爆量
            var action = l.Action.ToLowerInvariant();
            var (emoji, color, prefix) = action switch
            {
                "buy"      => ("🟢", 0x0ECB81, "Auto-Trader 買入"),
                "sell"     => ("🔴", 0xF6465D, "Auto-Trader 賣出"),
                "error"    => ("⚠️", 0xFCD535, "Auto-Trader 錯誤"),
                "blocked"  => ("🛑", 0xF6465D, "Auto-Trader 被風控擋下"),
                "adjusted" => ("✂️", 0xFCD535, "Auto-Trader 數量調整"),
                _          => ("", 0, ""),
            };
            if (emoji == "") continue;

            var fields = new[]
            {
                new { name = "Symbol",   value = l.Symbol,   inline = true },
                new { name = "Exchange", value = l.Exchange, inline = true },
                new { name = "Action",   value = action,     inline = true },
            };

            await SendEmbedAsync(
                title:       $"{emoji} {prefix} · {l.Symbol}",
                description: l.Message,
                color:       color,
                fields:      fields,
                timestamp:   l.Time,
                ct:          ct);
        }
    }

    // ── 對外 API（供 Endpoint 呼叫）───────────────────────────────────

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

    // ── 底層 webhook ─────────────────────────────────────────────────

    private async Task SendEmbedAsync(
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
            var resp = await client.PostAsJsonAsync(_webhookUrl, payload, ct);
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
