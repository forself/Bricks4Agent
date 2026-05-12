using System.Text;
using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Services;
using FunctionPool.Registry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Broker.Services;

/// <summary>
/// Daily PnL Report — 每天 UTC 00:00 自動彙總前 24h 交易績效、推 Discord + LINE。
///
/// 來源：trading.account/get_trade_history 純讀本地 DB、含 perp。
/// 算 win_rate / realized_pnl_sum / profit_factor、附 AutoTrader 當前狀態。
///
/// Schedule：
///   - DAILY_REPORT_AT_UTC_HOUR=0 預設、設成 -1 完全關閉自動推（仍可手動觸發 endpoint）
///   - 第一次跑會等到下個整點、避免容器重啟立刻爆訊息
/// </summary>
public class DailyReportService : BackgroundService
{
    private readonly IExecutionDispatcher _dispatcher;
    private readonly IWorkerRegistry _registry;
    private readonly DiscordNotificationService _discord;
    private readonly LineNotificationService _line;
    private readonly AutoTraderService _autoTrader;
    private readonly ILogger<DailyReportService> _logger;
    private readonly int _reportHourUtc;
    private readonly string _exchange;

    public DailyReportService(
        IExecutionDispatcher dispatcher,
        IWorkerRegistry registry,
        DiscordNotificationService discord,
        LineNotificationService line,
        AutoTraderService autoTrader,
        ILogger<DailyReportService> logger)
    {
        _dispatcher = dispatcher;
        _registry = registry;
        _discord = discord;
        _line = line;
        _autoTrader = autoTrader;
        _logger = logger;
        _reportHourUtc = ParseIntEnv("DAILY_REPORT_AT_UTC_HOUR", defaultValue: 0, min: -1, max: 23);
        _exchange = Environment.GetEnvironmentVariable("DAILY_REPORT_EXCHANGE") ?? "bingx";
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (_reportHourUtc < 0)
        {
            _logger.LogInformation("DailyReportService disabled (DAILY_REPORT_AT_UTC_HOUR=-1)");
            return;
        }

        _logger.LogInformation("DailyReportService scheduled at UTC {Hour}:00 daily, exchange={Exchange}",
            _reportHourUtc, _exchange);

        while (!ct.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var nextRun = new DateTime(now.Year, now.Month, now.Day, _reportHourUtc, 0, 0, DateTimeKind.Utc);
            if (nextRun <= now) nextRun = nextRun.AddDays(1);
            var delay = nextRun - now;
            _logger.LogDebug("DailyReport: next run at {Next} ({Delay} from now)", nextRun, delay);

            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) { break; }

            try
            {
                await BuildAndPushAsync(periodHours: 24, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DailyReport: build/push failed");
            }
        }
    }

    /// <summary>
    /// 組合過去 N 小時的成交摘要 + AutoTrader 狀態、推 Discord + LINE。
    /// 給手動端點 / scheduler 都用。
    /// </summary>
    public async Task<(bool Ok, string Summary)> BuildAndPushAsync(int periodHours, CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddHours(-periodHours);
        var stats = await GatherPnlStatsAsync(_exchange, since, ct);
        var watchSnapshot = _autoTrader.WatchList.Values
            .Where(w => w.Active)
            .Select(w => $"{w.Symbol}={w.Strategy} ({w.LastSignal ?? "?"}@{w.LastConfidence:P0})")
            .ToList();

        var pnlSign = stats.RealizedPnlSum >= 0 ? "+" : "";
        var color   = stats.RealizedPnlSum >= 0 ? 0x0ECB81 : 0xF6465D;
        var titleEmoji = stats.RealizedPnlSum >= 0 ? "📈" : "📉";

        var sb = new StringBuilder();
        sb.AppendLine($"**過去 {periodHours}h（{_exchange.ToUpper()}）**");
        sb.AppendLine($"成交筆數：{stats.TradeCount}  勝率：{stats.WinRatePct:F1}%  Profit Factor：{stats.ProfitFactor:F2}");
        sb.AppendLine($"已實現 PnL：**{pnlSign}{stats.RealizedPnlSum:F2}** USDT");
        sb.AppendLine($"勝/敗：{stats.WinCount}/{stats.LoseCount}  平均勝：+{stats.AvgWin:F2}  平均敗：{stats.AvgLoss:F2}");
        sb.AppendLine();
        sb.AppendLine($"**AutoTrader watch ({watchSnapshot.Count} active)**");
        foreach (var w in watchSnapshot.Take(8)) sb.AppendLine($"・{w}");

        var body = sb.ToString();
        var title = $"{titleEmoji} 每日交易彙整 · {DateTime.UtcNow:yyyy-MM-dd}";

        var dr = await _discord.SendAdHocAsync(title, body, color, ct);
        var lr = await _line.SendAdHocAsync(title, body,
            level: stats.RealizedPnlSum >= 0 ? "success" : "warning", ct);

        var ok = dr.ok || lr.ok;
        _logger.LogInformation("DailyReport pushed: discord={D} line={L} pnl={Pnl:F2} trades={N}",
            dr.ok, lr.ok, stats.RealizedPnlSum, stats.TradeCount);
        return (ok, body);
    }

    private async Task<PnlStats> GatherPnlStatsAsync(string exchange, DateTime sinceUtc, CancellationToken ct)
    {
        if (!_registry.HasAvailableWorker("trading.account"))
            return new PnlStats();   // worker 沒連、回 zero 不要 crash

        var payload = JsonSerializer.Serialize(new
        {
            exchange,
            limit = 500,
            since = sinceUtc.ToString("o"),
        });
        var req = new ApprovedRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            CapabilityId = "trading.account",
            Route = "get_trade_history",
            Payload = payload,
            Scope = "{}",
            PrincipalId = "system",   // 內部排程、不過 ACL
            TaskId = "daily-report",
            SessionId = "daily-report",
        };
        var result = await _dispatcher.DispatchAsync(req);
        if (!result.Success) return new PnlStats();

        var doc = JsonDocument.Parse(result.ResultPayload ?? "{}").RootElement;
        if (!doc.TryGetProperty("trades", out var trades) || trades.ValueKind != JsonValueKind.Array)
            return new PnlStats();

        int total = 0, wins = 0, loses = 0;
        decimal pnlSum = 0m, winSum = 0m, lossSum = 0m;
        foreach (var t in trades.EnumerateArray())
        {
            if (!t.TryGetProperty("realized_pnl", out var p) || p.ValueKind != JsonValueKind.Number) continue;
            var pnl = p.GetDecimal();
            total++; pnlSum += pnl;
            if (pnl > 0m) { wins++;  winSum  += pnl; }
            else if (pnl < 0m) { loses++; lossSum += pnl; }
        }
        return new PnlStats
        {
            TradeCount = total,
            WinCount = wins,
            LoseCount = loses,
            RealizedPnlSum = Math.Round(pnlSum, 4),
            WinRatePct = total > 0 ? Math.Round(100m * wins / total, 1) : 0m,
            AvgWin = wins > 0 ? Math.Round(winSum / wins, 4) : 0m,
            AvgLoss = loses > 0 ? Math.Round(lossSum / loses, 4) : 0m,
            ProfitFactor = lossSum < 0m ? Math.Round(winSum / Math.Abs(lossSum), 3) : (winSum > 0m ? 99.99m : 0m),
        };
    }

    private class PnlStats
    {
        public int TradeCount { get; set; }
        public int WinCount { get; set; }
        public int LoseCount { get; set; }
        public decimal RealizedPnlSum { get; set; }
        public decimal WinRatePct { get; set; }
        public decimal AvgWin { get; set; }
        public decimal AvgLoss { get; set; }
        public decimal ProfitFactor { get; set; }
    }

    private static int ParseIntEnv(string name, int defaultValue, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (int.TryParse(raw, out var v) && v >= min && v <= max) return v;
        return defaultValue;
    }
}
