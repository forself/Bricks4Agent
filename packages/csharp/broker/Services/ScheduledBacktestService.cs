using System.Collections.Concurrent;
using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;
using FunctionPool.Registry;

namespace Broker.Services;

/// <summary>
/// Composite ranking 權重。預設值跟 W14 demo 用的一樣、保留向後相容。
/// 想 tune 排名邏輯 → `Lab:Score:Sharpe`、`Lab:Score:Return` 等 config 個別覆寫、
/// 不用改 code 重 deploy。各權重不強制正規化到 1（讓人可加重某一面、再依結果調）。
/// </summary>
public record ScoreWeights(
    decimal Sharpe = 0.35m,
    decimal Return = 0.25m,
    decimal WinRate = 0.15m,
    decimal DrawdownPenalty = 0.1m,
    decimal Oos = 0.15m);

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

    /// <summary>
    /// 預設 24 條 — strategy-worker 註冊的所有 deterministic + meta 策略，排除：
    ///   - "llm"            燒 LLM token、貴、單獨用 manual /optimize 跑
    ///   - "news_sentiment"  同上、且依賴外部新聞 API
    /// 為什麼全部納入：之前寫死 4 條（sma_cross/rsi_oversold/macd_divergence/composite）、
    /// 其它 20 條從未進入 recommended pool —— dashboard 推薦永遠只能在 4 條裡選、看不出新策略表現。
    /// 行情 / 標的會變、最佳策略也跟著變、batch 應該掃全套讓 ranking 真實。
    ///
    /// 想換子集 → appsettings.json `Lab:Strategies: ["sma_cross","rsi_oversold",...]` 整個覆寫。
    /// </summary>
    private static readonly string[] DefaultStrategies = {
        // 3 條有 grid search optimizer（/optimize 路徑、tune 過 params）
        "sma_cross", "rsi_oversold", "macd_divergence",
        // Meta / combined（值得單獨追蹤是否真的超越成員）
        "composite", "ensemble", "auto_select",
        // 標準技術指標
        "multi_timeframe", "fibonacci_retracement", "bollinger_bands",
        "harmonic_pattern", "vegas_tunnel", "price_action",
        // Batch A 從朋友 ai-quant-starter2 移植
        "super_trend", "adx_di", "ichimoku", "rsi_stoch", "vwap",
        // Tier 2 batch
        "donchian", "keltner", "parabolic_sar",
        "cci", "obv", "mfi", "chaikin_mf",
    };
    private readonly string[] _strategies;

    /// <summary>
    /// recommendation gate：trades 少於這數的策略不算入 recommended pool。
    /// 避免 1-2 筆 lucky trade 拿超高 sharpe / win_rate 被誤推薦、實際樣本不足。
    /// 預設 3 — 跟 walk-forward 5 folds 對齊。可用 `Lab:MinTrades` 覆寫。
    /// </summary>
    private readonly int _minTrades;

    /// <summary>Composite score 權重（A1：外露 config、tune 排名不用改 code）</summary>
    private readonly ScoreWeights _scoreWeights;

    /// <summary>
    /// 同 (symbol, timeframe) 內、平行跑 N 個 strategy 的 max concurrency（A3）。
    /// 預設 4 — 對 strategy-worker dispatch（local CPU compute）算保守。
    /// 1 = 退回 sequential（demo / debug 用）。可用 `Lab:MaxParallel` 覆寫。
    /// 已配 ConcurrentBag + Interlocked.Increment，allResults / errors 寫入安全。
    /// </summary>
    private readonly int _maxParallel;

    // 從 200 拉到 500、訊號穩定度提升、low-sample tag 會大幅減少。
    // BingX 1h/4h 都有 500+ 根、1d 看歷史長度但 quote-worker 已經抓 365 根 daily、夠用。
    private const int BarsPerBacktest = 500;

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

        _strategies = ResolveStrategies(config, DefaultStrategies);
        _minTrades = ResolveMinTrades(config);
        _scoreWeights = ResolveScoreWeights(config);
        _maxParallel = ResolveMaxParallel(config);
    }

    /// <summary>
    /// 從 config 解 Lab:Strategies、空 / 未設都退回 defaults（防誤設 [] 變零策略）。
    /// internal 給 unit test 用、不用真的 new 整個 service。
    /// </summary>
    internal static string[] ResolveStrategies(IConfiguration config, string[] defaults)
    {
        var configured = config.GetSection("Lab:Strategies").Get<string[]>();
        return (configured != null && configured.Length > 0) ? configured : defaults;
    }

    /// <summary>
    /// Min trades gate：預設 3、可下修到 1（demo / dev 想看小樣本結果）。
    /// 0 / 負數 / 未設 → clamp 到合理範圍、避免「全 fan」推薦。
    /// </summary>
    internal static int ResolveMinTrades(IConfiguration config)
        => Math.Max(1, config.GetValue("Lab:MinTrades", 3));

    /// <summary>
    /// 從 config 解 ScoreWeights、未設用預設值（向後相容、跟 W14 demo 那版分數相同）。
    /// 各權重設負數視為 0（防誤設變懲罰）。
    /// </summary>
    internal static ScoreWeights ResolveScoreWeights(IConfiguration config)
    {
        var d = new ScoreWeights();
        decimal Clamp(string key, decimal def)
            => Math.Max(0m, config.GetValue($"Lab:Score:{key}", def));
        return new ScoreWeights(
            Sharpe:          Clamp("Sharpe", d.Sharpe),
            Return:          Clamp("Return", d.Return),
            WinRate:         Clamp("WinRate", d.WinRate),
            DrawdownPenalty: Clamp("DrawdownPenalty", d.DrawdownPenalty),
            Oos:             Clamp("Oos", d.Oos));
    }

    /// <summary>
    /// Max parallelism：預設 4、clamp [1, 16]。1 = sequential、16 上限是避免有人寫 999
    /// 把 strategy-worker 灌爆。
    /// </summary>
    internal static int ResolveMaxParallel(IConfiguration config)
        => Math.Clamp(config.GetValue("Lab:MaxParallel", 4), 1, 16);

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
        // 整輪 backtest 共用一個 trace_id——這樣 /audit/traces 上會看到「一個 run 包含 N 次
        // quote.ohlcv + N 次 strategy.signal」的長條 trace，而不是 N 個獨立 trace。
        // 是 fan-out tracing 的展示點。
        var traceId = $"trc_{runId}";
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
                var barsOpt = await FetchBarsAsync(w.Symbol, tf, BarsPerBacktest, traceId, ct);
                var barsCount = barsOpt.HasValue ? barsOpt.Value.GetArrayLength() : 0;
                if (!barsOpt.HasValue || barsCount < 50)
                {
                    foreach (var strat in _strategies)
                        allResults.Add(new BacktestResultEntry
                        {
                            RunId = runId, Symbol = w.Symbol, Exchange = w.Exchange,
                            Timeframe = tf, Strategy = strat, BarsCount = barsCount,
                            Error = "insufficient bars",
                        });
                    continue;
                }

                // 為這個 (symbol, timeframe) 對應的 bars 窗口算一次 regime、所有策略共用
                var regime = ClassifyRegime(barsOpt.Value);

                // A3：同 (symbol, tf) 內所有策略平行跑（策略間互不影響、共用 bars）。
                // _maxParallel=1 退回 sequential、4 = ~1/4 時間、可調 1-16。
                // ConcurrentBag + Interlocked 保證 multi-thread 寫入安全。
                var batchBag = new ConcurrentBag<BacktestResultEntry>();
                int batchErrors = 0;
                await Parallel.ForEachAsync(_strategies,
                    new ParallelOptions { MaxDegreeOfParallelism = _maxParallel, CancellationToken = ct },
                    async (strat, innerCt) =>
                    {
                        var entry = await RunSingleBacktestAsync(runId, w.Symbol, w.Exchange, tf, strat, barsOpt.Value, traceId, innerCt);
                        entry.Regime = regime;
                        entry.OwnerPrincipalId = string.IsNullOrEmpty(w.OwnerPrincipalId) ? "prn_dashboard" : w.OwnerPrincipalId;
                        if (!string.IsNullOrEmpty(entry.Error)) Interlocked.Increment(ref batchErrors);
                        batchBag.Add(entry);
                    });
                allResults.AddRange(batchBag);
                errors += batchErrors;
            }
        }

        // ranking：每 (symbol, timeframe) 找 score 最高的標 recommended
        // 三層過濾：
        //   1. 沒 error 且 Trades >= MinTrades（樣本不足拒推薦、防 lucky 1-trade）
        //   2. WfFolds>0 且 |IsOosGap|>=0.7 → 排除（過擬合紅線、不論 IS 多漂亮都不該被推薦）
        //   3. WfFolds=0（沒跑成功）→ 不擋（向後相容、bars 不夠也能用 IS-only ranking）
        var grouped = allResults
            .Where(r => string.IsNullOrEmpty(r.Error) && r.Trades >= _minTrades)
            .Where(r => r.WfFolds == 0 || Math.Abs(r.IsOosGap) < 0.7m)
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
            runId, perpWatches.Count, Timeframes.Length, _strategies.Length, allResults.Count, errors, run.DurationMs);
        return runId;
    }

    private async Task<JsonElement?> FetchBarsAsync(string symbol, string interval, int limit, string traceId, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { symbol, interval, limit });
        var req = new ApprovedRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            CapabilityId = "quote.ohlcv", Route = "get_bars", Payload = payload,
            Scope = "{}", PrincipalId = "system",
            TaskId = "scheduled-backtest", SessionId = "scheduled-backtest",
            TraceId = traceId,
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

    /// <summary>有 grid search optimizer 的策略——這些走 /optimize 找最佳 params；其他用 default 跑 /backtest。</summary>
    private static readonly HashSet<string> OptimizableStrategies = new(StringComparer.OrdinalIgnoreCase)
    {
        "sma_cross", "rsi_oversold", "macd_divergence",
    };

    private async Task<BacktestResultEntry> RunSingleBacktestAsync(
        string runId, string symbol, string exchange, string tf, string strategy, JsonElement bars, string traceId, CancellationToken ct)
    {
        var entry = new BacktestResultEntry
        {
            RunId = runId, Symbol = symbol, Exchange = exchange,
            Timeframe = tf, Strategy = strategy, BarsCount = bars.GetArrayLength(),
        };

        var useOptimizer = OptimizableStrategies.Contains(strategy);
        var route = useOptimizer ? "optimize" : "backtest";

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
            CapabilityId = "strategy.signal", Route = route, Payload = payload,
            Scope = "{}", PrincipalId = "system",
            TaskId = "scheduled-backtest", SessionId = "scheduled-backtest",
            TraceId = traceId,
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
            if (useOptimizer)
            {
                // /optimize 回 best_params + top_results[]，取 top_results[0] 拿完整指標
                if (doc.TryGetProperty("best_params", out var bp) && bp.ValueKind == JsonValueKind.Object)
                    entry.ParamsJson = bp.GetRawText();

                if (doc.TryGetProperty("top_results", out var tops) && tops.ValueKind == JsonValueKind.Array && tops.GetArrayLength() > 0)
                {
                    var best = tops[0];
                    entry.TotalReturnPct = best.TryGetProperty("total_return_pct", out var tr) ? tr.GetDecimal() : 0m;
                    entry.Sharpe = best.TryGetProperty("sharpe", out var sh) ? sh.GetDecimal() : 0m;
                    entry.WinRate = best.TryGetProperty("win_rate", out var wr) ? wr.GetDecimal() : 0m;
                    entry.MaxDdPct = best.TryGetProperty("max_drawdown_pct", out var dd) ? dd.GetDecimal() : 0m;
                    entry.Trades = best.TryGetProperty("trades", out var t) ? t.GetInt32() : 0;
                }
            }
            else
            {
                // /backtest 回平面欄位、注意 sharpe_ratio / total_trades 跟 /optimize 不同
                entry.TotalReturnPct = doc.TryGetProperty("total_return_pct", out var tr) ? tr.GetDecimal() : 0m;
                entry.Sharpe = doc.TryGetProperty("sharpe_ratio", out var sh) ? sh.GetDecimal() : 0m;
                entry.WinRate = doc.TryGetProperty("win_rate", out var wr) ? wr.GetDecimal() : 0m;
                entry.MaxDdPct = doc.TryGetProperty("max_drawdown_pct", out var dd) ? dd.GetDecimal() : 0m;
                entry.Trades = doc.TryGetProperty("total_trades", out var t) ? t.GetInt32() : 0;
            }
            entry.Score = ComputeScore(entry, _scoreWeights);
        }
        catch (Exception ex)
        {
            entry.Error = ex.Message?.Length > 200 ? ex.Message[..200] : ex.Message;
            return entry;
        }

        // B3：跑 walk-forward 拿 OOS 數據（額外一次 strategy.signal 呼叫）
        // 失敗 / bars 不夠 → 維持 IS-only、WfFolds=0、OOS 欄位 = 0、不擋
        try
        {
            await RunWalkForwardAsync(entry, symbol, exchange, tf, strategy, bars, traceId, ct);
            // 跑成功 → 用 IS+OOS 重新算 score
            entry.Score = ComputeScore(entry, _scoreWeights);
        }
        catch (Exception ex)
        {
            // 不覆寫 entry.Error（IS 已成功）、log 即可
            _logger.LogDebug(ex, "Walk-forward extension failed for {Sym}/{Tf}/{Strat}", symbol, tf, strategy);
        }

        return entry;
    }

    /// <summary>
    /// 跑 strategy.signal 的 backtest_walk_forward route、把 OOS 數據填回 entry。
    /// 預設 train=180 / test=60 / stride=30。500 bars 大概切出 5 個 fold。
    /// </summary>
    private async Task RunWalkForwardAsync(
        BacktestResultEntry entry, string symbol, string exchange, string tf,
        string strategy, JsonElement bars, string traceId, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            strategy, symbol, exchange, interval = tf, bars,
            initial_cash = 1000.0,
            train_bars = 180,
            test_bars = 60,
            stride = 30,
        });
        var req = new ApprovedRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            CapabilityId = "strategy.signal", Route = "backtest_walk_forward", Payload = payload,
            Scope = "{}", PrincipalId = "system",
            TaskId = "scheduled-backtest", SessionId = "scheduled-backtest",
            TraceId = traceId,
        };
        var result = await _dispatcher.DispatchAsync(req);
        if (!result.Success) return;  // bars 不夠等失敗 → 略過

        var doc = JsonDocument.Parse(result.ResultPayload ?? "{}").RootElement;
        entry.WfFolds      = doc.TryGetProperty("total_folds",          out var tf0) ? tf0.GetInt32()    : 0;
        entry.OosReturnPct = doc.TryGetProperty("avg_test_return_pct",  out var ar)  ? ar.GetDecimal()   : 0m;
        entry.OosSharpe    = doc.TryGetProperty("avg_test_sharpe",      out var asr) ? asr.GetDecimal()  : 0m;
        entry.OosWinRate   = doc.TryGetProperty("avg_test_win_rate",    out var awr) ? awr.GetDecimal()  : 0m;
        entry.IsOosGap     = doc.TryGetProperty("is_oos_return_gap",    out var iog) ? iog.GetDecimal()  : 0m;
    }

    /// <summary>
    /// composite ranking: Sharpe·sharpe_norm + Return·return_norm + WinRate·win_rate_norm
    ///                  − DrawdownPenalty·dd_penalty + Oos·oos_quality
    /// 預設 ScoreWeights = (0.35, 0.25, 0.15, 0.1, 0.15) 跟 W14 demo 時相同、向後相容。
    /// 想 tune：appsettings.json `Lab:Score:Sharpe = 0.5` 等個別覆寫、不用改 code。
    ///
    /// OOS 沒跑 → oos_quality=0、退化成 IS-only。Walk-forward 跑成功時納入過擬合懲罰。
    /// 沒交易直接 0 分（拒列入 recommended）。
    /// internal static 給 unit test 用 + ScoreWeights 注入。
    /// </summary>
    internal static decimal ComputeScore(BacktestResultEntry r, ScoreWeights w)
    {
        if (r.Trades == 0) return 0m;
        var sharpeNorm = Math.Clamp((r.Sharpe + 2m) / 7m, 0m, 1m);
        var returnNorm = Math.Clamp(r.TotalReturnPct / 100m, 0m, 1m);
        var winRateNorm = Math.Clamp(r.WinRate / 100m, 0m, 1m);
        var ddPenalty = Math.Clamp(r.MaxDdPct / 50m, 0m, 1m);

        var baseScore = w.Sharpe * sharpeNorm + w.Return * returnNorm
                      + w.WinRate * winRateNorm - w.DrawdownPenalty * ddPenalty;

        // OOS quality factor：walk-forward 跑成功才加分
        //   高 IS-OOS gap (>= 0.5) → 紅旗、加負分
        //   低 gap + 正 OOS sharpe → 加正分
        //   沒跑 (WfFolds=0) → 不加不扣
        decimal oosQuality = 0m;
        if (r.WfFolds > 0)
        {
            var oosSharpeNorm = Math.Clamp((r.OosSharpe + 2m) / 7m, 0m, 1m);
            var gapPenalty    = Math.Clamp(Math.Abs(r.IsOosGap), 0m, 1m);
            oosQuality = (oosSharpeNorm - 0.5m) - gapPenalty * 0.5m;
        }

        return Math.Round(baseScore + w.Oos * oosQuality, 4);
    }

    /// <summary>
    /// 把 bars 窗口分類成市場行情類型——讓 lab/recommendations 將來可以按 regime 過濾、
    /// 「現在是震盪市、推薦 mean-reversion 類；現在是趨勢市、推薦 trend-follow 類」。
    ///
    /// 簡化版判斷（之後可再升級用真 ADX）：
    ///   - close 變動方向一致性（trend strength proxy）：(end - start) / range_total
    ///   - 平均 daily range %（atr proxy）：mean((high - low) / open)
    ///
    /// 規則：
    ///   trending  : trend_strength &gt; 0.4
    ///   volatile  : avg_range_pct &gt; 5%
    ///   squeeze   : avg_range_pct &lt; 1.5% AND trend_strength &lt; 0.2
    ///   ranging   : 其他（震盪 / 沒明顯方向）
    /// </summary>
    private static string ClassifyRegime(JsonElement bars)
    {
        var n = bars.GetArrayLength();
        if (n < 20) return "unknown";

        decimal firstClose = 0m, lastClose = 0m;
        decimal totalRange = 0m;     // sum of |close[i] - close[i-1]|
        decimal sumRangePct = 0m;    // sum of (high-low)/open
        decimal prevClose = 0m;
        int idx = 0;
        foreach (var b in bars.EnumerateArray())
        {
            var open = b.TryGetProperty("open", out var o) ? o.GetDecimal() : 0m;
            var high = b.TryGetProperty("high", out var h) ? h.GetDecimal() : 0m;
            var low = b.TryGetProperty("low", out var l) ? l.GetDecimal() : 0m;
            var close = b.TryGetProperty("close", out var c) ? c.GetDecimal() : 0m;

            if (idx == 0) firstClose = close;
            lastClose = close;
            if (open > 0m) sumRangePct += (high - low) / open;
            if (idx > 0) totalRange += Math.Abs(close - prevClose);
            prevClose = close;
            idx++;
        }

        if (firstClose <= 0m || idx == 0) return "unknown";
        var netMove = Math.Abs(lastClose - firstClose);
        var trendStrength = totalRange > 0m ? (decimal)Math.Abs((double)(netMove / totalRange)) : 0m;
        var avgRangePct = sumRangePct / idx * 100m;

        if (trendStrength > 0.4m) return "trending";
        if (avgRangePct > 5m) return "volatile";
        if (avgRangePct < 1.5m && trendStrength < 0.2m) return "squeeze";
        return "ranging";
    }
}
