using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;
using FunctionPool.Registry;

namespace Broker.Services;

/// <summary>
/// 自動排程的批次回測服務——每天醒來一次、把所有 watched perpetual symbols 跨多 timeframe
/// 跨多策略全部跑一輪、結果落 DB、最佳組合標 recommended、user 用 /lab/recommendations 查。
///
/// 為什麼要這樣：手動戳 /optimize 是「我得先想要什麼問題、再去問」。但策略要長期跑、市場
/// 行情會切換、最適策略也跟著變。讓服務自己每天定時跑、user 想看 latest insight 直接查 DB、
/// 不用每次 redo grid search。
///
/// 跑什麼：
///   - 標的：所有 watched 的 perp watches（mode=perp_*）
///   - timeframe：1h、4h、1d（quote-worker 都有抓）
///   - 策略：sma_cross / rsi_oversold / macd_divergence / composite
///   - 輸入：每組 200 根 K 線（足夠算 SMA(30)、RSI(14) 等指標、不會太久）
///
/// 如何 rank：
///   composite score = 0.4 × sharpe + 0.3 × normalized_return + 0.2 × win_rate − 0.1 × dd_penalty
///   每個 (symbol, timeframe) 找最高分、標 recommended。
///
/// 為什麼 capital_fit 不在這裡濾：策略本身的「最低資金」是策略屬性、放在 LabEndpoints 查詢時
/// 用、不需要每次 backtest 都重算。
/// </summary>
public class ScheduledBacktestService : BackgroundService
{
    private readonly IExecutionDispatcher _dispatcher;
    private readonly IWorkerRegistry _registry;
    private readonly BrokerDb _db;
    private readonly AutoTraderService _autoTrader;
    private readonly ILogger<ScheduledBacktestService> _logger;
    private readonly TimeSpan _interval;
    private readonly bool _runOnStart;

    private static readonly string[] Timeframes = { "1h", "4h", "1d" };
    private static readonly string[] Strategies = { "sma_cross", "rsi_oversold", "macd_divergence", "composite" };
    private const int BarsPerBacktest = 200;

    public ScheduledBacktestService(
        IExecutionDispatcher dispatcher,
        IWorkerRegistry registry,
        BrokerDb db,
        AutoTraderService autoTrader,
        IConfiguration config,
        ILogger<ScheduledBacktestService> logger)
    {
        _dispatcher = dispatcher;
        _registry = registry;
        _db = db;
        _autoTrader = autoTrader;
        _logger = logger;
        var hours = Math.Max(1, config.GetValue("Lab:ScheduledIntervalHours", 24));
        _interval = TimeSpan.FromHours(hours);
        _runOnStart = config.GetValue("Lab:RunOnStart", false);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("ScheduledBacktest service started, interval={Hours}h, runOnStart={RunOnStart}",
            _interval.TotalHours, _runOnStart);

        if (_runOnStart)
        {
            try { await RunOnceAsync("scheduled", ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Initial scheduled run failed"); }
        }

        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(_interval, ct); }
            catch (TaskCanceledException) { break; }

            try { await RunOnceAsync("scheduled", ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Scheduled run failed"); }
        }
    }

    public async Task<string> RunOnceAsync(string runType, CancellationToken ct)
    {
        var runId = $"run_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        var startedAt = DateTime.UtcNow;
        var run = new BacktestRunEntry { RunId = runId, StartedAt = startedAt, RunType = runType };
        _db.Insert(run);

        // 拿 perp watches（mode=perp_*）
        var perpWatches = _autoTrader.WatchList.Values
            .Where(w => w.Mode == "perp_long_only" || w.Mode == "perp_both")
            .ToList();

        if (perpWatches.Count == 0)
        {
            run.FinishedAt = DateTime.UtcNow;
            run.Notes = "No perp watches configured";
            _db.Update(run);
            _logger.LogInformation("Backtest run {RunId}: no perp watches, skipped", runId);
            return runId;
        }

        if (!_registry.HasAvailableWorker("strategy.signal") || !_registry.HasAvailableWorker("quote.ohlcv"))
        {
            run.FinishedAt = DateTime.UtcNow;
            run.Notes = "strategy.signal or quote.ohlcv worker offline";
            _db.Update(run);
            _logger.LogWarning("Backtest run {RunId}: worker offline, skipped", runId);
            return runId;
        }

        var allResults = new List<BacktestResultEntry>();
        int errors = 0;
        foreach (var w in perpWatches)
        {
            foreach (var tf in Timeframes)
            {
                var barsOpt = await FetchBarsAsync(w.Symbol, tf, BarsPerBacktest, ct);
                var barsCount = barsOpt.HasValue ? barsOpt.Value.GetArrayLength() : 0;
                if (!barsOpt.HasValue || barsCount < 50)
                {
                    foreach (var strat in Strategies)
                        allResults.Add(new BacktestResultEntry
                        {
                            RunId = runId, Symbol = w.Symbol, Exchange = w.Exchange,
                            Timeframe = tf, Strategy = strat, BarsCount = barsCount,
                            Error = "insufficient bars",
                        });
                    continue;
                }

                foreach (var strat in Strategies)
                {
                    if (ct.IsCancellationRequested) break;
                    var entry = await RunSingleBacktestAsync(runId, w.Symbol, w.Exchange, tf, strat, barsOpt.Value, ct);
                    if (!string.IsNullOrEmpty(entry.Error)) errors++;
                    allResults.Add(entry);
                }
            }
        }

        // ranking：每 (symbol, timeframe) 找 score 最高的標 recommended
        var grouped = allResults
            .Where(r => string.IsNullOrEmpty(r.Error) && r.Trades > 0)
            .GroupBy(r => (r.Symbol, r.Timeframe));
        foreach (var g in grouped)
        {
            var best = g.OrderByDescending(r => r.Score).FirstOrDefault();
            if (best != null) best.Recommended = true;
        }

        // 全部寫入
        foreach (var r in allResults) _db.Insert(r);

        run.FinishedAt = DateTime.UtcNow;
        run.DurationMs = (long)(run.FinishedAt.Value - run.StartedAt).TotalMilliseconds;
        run.SymbolsCount = perpWatches.Count;
        run.ResultsCount = allResults.Count;
        run.ErrorCount = errors;
        _db.Update(run);

        _logger.LogInformation("Backtest run {RunId} done: {Symbols} symbols × {Tf} tf × {Strat} strat = {Total} results, {Errors} errors, {Ms}ms",
            runId, perpWatches.Count, Timeframes.Length, Strategies.Length, allResults.Count, errors, run.DurationMs);
        return runId;
    }

    private async Task<JsonElement?> FetchBarsAsync(string symbol, string interval, int limit, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { symbol, interval, limit });
        var req = new ApprovedRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            CapabilityId = "quote.ohlcv", Route = "get_bars", Payload = payload,
            Scope = "{}", PrincipalId = "system",
            TaskId = "scheduled-backtest", SessionId = "scheduled-backtest",
        };
        var result = await _dispatcher.DispatchAsync(req);
        if (!result.Success) return null;
        try
        {
            var doc = JsonDocument.Parse(result.ResultPayload ?? "{}").RootElement;
            return doc.TryGetProperty("bars", out var bars) ? bars : null;
        }
        catch { return null; }
    }

    private async Task<BacktestResultEntry> RunSingleBacktestAsync(
        string runId, string symbol, string exchange, string tf, string strategy, JsonElement bars, CancellationToken ct)
    {
        var entry = new BacktestResultEntry
        {
            RunId = runId, Symbol = symbol, Exchange = exchange,
            Timeframe = tf, Strategy = strategy, BarsCount = bars.GetArrayLength(),
        };

        var payload = JsonSerializer.Serialize(new
        {
            strategy, symbol, exchange,
            interval = tf,
            bars,
            initial_cash = 1000.0,
        });

        var req = new ApprovedRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            CapabilityId = "strategy.signal", Route = "backtest", Payload = payload,
            Scope = "{}", PrincipalId = "system",
            TaskId = "scheduled-backtest", SessionId = "scheduled-backtest",
        };

        var result = await _dispatcher.DispatchAsync(req);
        if (!result.Success)
        {
            entry.Error = result.ErrorMessage?.Length > 200 ? result.ErrorMessage[..200] : result.ErrorMessage;
            return entry;
        }

        try
        {
            var doc = JsonDocument.Parse(result.ResultPayload ?? "{}").RootElement;
            // strategy worker 回 total_return_pct / sharpe_ratio / win_rate / max_drawdown_pct / total_trades；
            // 'trades' 在 response 是 trade list array（單筆交易明細）、別跟總筆數混淆。
            entry.TotalReturnPct = doc.TryGetProperty("total_return_pct", out var tr) ? tr.GetDecimal() : 0m;
            entry.Sharpe = doc.TryGetProperty("sharpe_ratio", out var sh) ? sh.GetDecimal() : 0m;
            entry.WinRate = doc.TryGetProperty("win_rate", out var wr) ? wr.GetDecimal() : 0m;
            entry.MaxDdPct = doc.TryGetProperty("max_drawdown_pct", out var dd) ? dd.GetDecimal() : 0m;
            entry.Trades = doc.TryGetProperty("total_trades", out var t) ? t.GetInt32() : 0;
            entry.Score = ComputeScore(entry);
        }
        catch (Exception ex)
        {
            entry.Error = ex.Message?.Length > 200 ? ex.Message[..200] : ex.Message;
        }

        return entry;
    }

    /// <summary>
    /// composite ranking: 0.4·sharpe + 0.3·return_norm + 0.2·win_rate − 0.1·dd_penalty。
    /// sharpe 假設 [-2, +5] 線性 norm 到 [0,1]；return [0,100%] norm；dd_penalty 越大扣越多。
    /// 沒交易直接 0 分（拒列入 recommended）。
    /// </summary>
    private static decimal ComputeScore(BacktestResultEntry r)
    {
        if (r.Trades == 0) return 0m;
        var sharpeNorm = Math.Clamp((r.Sharpe + 2m) / 7m, 0m, 1m);
        var returnNorm = Math.Clamp(r.TotalReturnPct / 100m, 0m, 1m);
        var winRateNorm = Math.Clamp(r.WinRate / 100m, 0m, 1m);
        var ddPenalty = Math.Clamp(r.MaxDdPct / 50m, 0m, 1m);
        return Math.Round(
            0.4m * sharpeNorm + 0.3m * returnNorm + 0.2m * winRateNorm - 0.1m * ddPenalty,
            4);
    }
}
