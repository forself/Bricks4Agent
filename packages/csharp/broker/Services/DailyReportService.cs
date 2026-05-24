using System.Text;
using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Services;
using BrokerCore.Trading;
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

            // 每天都跑 24h；週日加跑 weekly (168h)、月初 1 號加跑 monthly (30d ≈ 720h)
            var fired = DateTime.UtcNow;
            try
            {
                await BuildAndPushAsync(periodHours: 24, ct);

                if (fired.DayOfWeek == DayOfWeek.Sunday)
                {
                    await BuildAndPushAsync(periodHours: 24 * 7, ct);
                }
                if (fired.Day == 1)
                {
                    // 月初 1 號：粗略用 30 天當「上個月」、不用真實天數（差 1-2 天 narrative 可接受）
                    await BuildAndPushAsync(periodHours: 24 * 30, ct);
                }
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
        var fetched = await FetchTradesAsync(_exchange, since, ct);
        var stats = PnlAggregator.Aggregate(fetched.Select(t => t.Pnl));

        // perp 平倉 income 沒帶 strategy(交易所 income 只給 symbol)→ 用 AutoTrader watchlist 的
        // symbol→策略補上(正規化去 - / 大寫,避免 BNB-USDT vs BNBUSDT 對不上)。
        // 用「當前」對照:同幣日後換策略,舊紀錄歸屬會跟著動(現行穩定配置下可接受)。
        static string Norm(string s) => s.Replace("-", "").Replace("/", "").ToUpperInvariant();
        var symToStrat = _autoTrader.WatchList.Values
            .Where(w => !string.IsNullOrEmpty(w.Strategy))
            .GroupBy(w => Norm(w.Symbol))
            .ToDictionary(g => g.Key, g => g.First().Strategy!);
        string ResolveStrat((decimal Pnl, string? Strategy, string? Symbol) t)
        {
            if (!string.IsNullOrEmpty(t.Strategy)) return t.Strategy!;
            if (!string.IsNullOrEmpty(t.Symbol) && symToStrat.TryGetValue(Norm(t.Symbol!), out var st)) return st;
            return "(無)";
        }

        var byStrategy = fetched
            .GroupBy(ResolveStrat)
            .Select(g => new { Name = g.Key, Stats = PnlAggregator.Aggregate(g.Select(x => x.Pnl)) })
            .OrderByDescending(x => x.Stats.RealizedPnlSum)
            .ToList();

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

        if (byStrategy.Count > 0 && byStrategy.Any(x => x.Stats.TradeCount > 0))
        {
            sb.AppendLine();
            sb.AppendLine($"**按策略（{byStrategy.Count} 組）**");
            foreach (var s in byStrategy.Take(6))
            {
                if (s.Stats.TradeCount == 0) continue;
                var sign = s.Stats.RealizedPnlSum >= 0 ? "+" : "";
                sb.AppendLine($"・{s.Name}: {s.Stats.TradeCount}筆 勝率{s.Stats.WinRatePct:F0}% PnL **{sign}{s.Stats.RealizedPnlSum:F2}** PF {s.Stats.ProfitFactor:F2}");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"**AutoTrader watch ({watchSnapshot.Count} active)**");
        foreach (var w in watchSnapshot.Take(8)) sb.AppendLine($"・{w}");

        var body = sb.ToString();
        var title = $"{titleEmoji} 每日交易彙整 · {DateTime.UtcNow:yyyy-MM-dd}";

        var dr = await _discord.SendAdHocAsync(title, body, color, ct);
        var lr = await _line.SendAdHocAsync(title, body,
            level: stats.RealizedPnlSum >= 0 ? "success" : "warning", ct);

        var ok = dr.ok || lr.ok;
        _logger.LogInformation("DailyReport pushed: discord={D} line={L} pnl={Pnl:F2} trades={N} strategies={S}",
            dr.ok, lr.ok, stats.RealizedPnlSum, stats.TradeCount, byStrategy.Count);
        return (ok, body);
    }

    private async Task<List<(decimal Pnl, string? Strategy, string? Symbol)>> FetchTradesAsync(
        string exchange, DateTime sinceUtc, CancellationToken ct)
    {
        var empty = new List<(decimal, string?, string?)>();
        if (!_registry.HasAvailableWorker("trading.account")) return empty;

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
        if (!result.Success) return empty;

        var doc = JsonDocument.Parse(result.ResultPayload ?? "{}").RootElement;
        if (!doc.TryGetProperty("trades", out var trades) || trades.ValueKind != JsonValueKind.Array)
            return empty;

        var list = new List<(decimal, string?, string?)>();
        foreach (var t in trades.EnumerateArray())
        {
            if (!t.TryGetProperty("realized_pnl", out var p) || p.ValueKind != JsonValueKind.Number) continue;
            var pnl = p.GetDecimal();
            var strategy = t.TryGetProperty("strategy", out var sg) && sg.ValueKind == JsonValueKind.String
                ? sg.GetString() : null;
            var symbol = t.TryGetProperty("symbol", out var sy) && sy.ValueKind == JsonValueKind.String
                ? sy.GetString() : null;
            list.Add((pnl, strategy, symbol));
        }
        return list;
    }

    private static int ParseIntEnv(string name, int defaultValue, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (int.TryParse(raw, out var v) && v >= min && v <= max) return v;
        return defaultValue;
    }
}
