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
    decimal Sharpe = 0.30m,           // OOS sharpe（risk-adjusted）
    decimal Return = 0.40m,           // OOS 報酬（真正賺不賺 = 主軸）
    decimal WinRate = 0.05m,          // OOS 勝率（刻意降權：高勝率小賺 ≠ 好策略）
    decimal DrawdownPenalty = 0.1m,
    decimal Oos = 0.15m);             // overfit 懲罰權重（IS-OOS gap）

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

    private static readonly string[] Timeframes = { "15m", "30m", "1h", "2h", "4h", "1d", "3d", "1w" };

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
    // 大量回測 batch = 只跑「deterministic 純技術」策略。刻意排除會碰 LLM 的：
    //   - "ensemble"：用 LLM 仲裁器;LLM upstream 一 503 就拖垮整條 worker connection
    //     （"Connection closing" → 整批 cascade fail，5/21 實測 1582/1584 fail）。
    //   - "auto_select"：meta router、對 ranking 加分有限。
    //   - "llm" / "news_sentiment"：燒 token + 依賴外部 API + 非決定性。
    // 這些是「live 訊號」用、不是「系統化回測排名」用。ensemble/auto_select 要評估走 manual。
    // 換成新的決定性技術策略（smc / regime_adaptive）進 ranking pool。
    private static readonly string[] DefaultStrategies = {
        // 3 條有 grid search optimizer
        "sma_cross", "rsi_oversold", "macd_divergence",
        // Meta（純技術、無 LLM）
        "composite", "regime_adaptive", "character_ensemble",
        // 標準技術指標
        "multi_timeframe", "fibonacci_retracement", "bollinger_bands",
        "harmonic_pattern", "vegas_tunnel", "price_action", "smc",
        // Batch A ai-quant-starter2 移植
        "super_trend", "adx_di", "ichimoku", "rsi_stoch", "vwap",
        // Tier 2 batch
        "donchian", "keltner", "parabolic_sar",
        "cci", "obv", "mfi", "chaikin_mf",
        // 正交因子策略（Hurst 記憶性 / 波動率突破）
        "hurst_adaptive", "volatility_breakout",
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

    /// <summary>
    /// 回測 universe（跟即時交易 watch list 解耦）。這些 symbol 只拿來「分析回測」、不會被 AutoTrader 交易。
    /// 跟 perp watches 取聯集。可用 `Lab:BacktestSymbols`（逗號分隔、BingX 格式 "BTC-USDT"）覆寫。
    /// </summary>
    private readonly string[] _backtestSymbols;

    // 1000 bars：配合深度回補的歷史（1d 1500 / 4h 1500 / 1h 2000）做認真的策略篩選。
    // 史記：曾因 framing bug 降到 200；root cause 兩個都已修：
    //   1) ensemble 的 LLM 502 污染 worker connection（LlmEnsembleArbitrator circuit breaker）。
    //   2) worker-sdk 並發寫 frame 沒鎖（WorkerHost._writeLock）。
    // 修完 parallel + 大 payload 已安全。1000 bars 給 walk-forward 切出足夠 fold、又不超過任一 tf 的存量。
    private const int BarsPerBacktest = 1000;

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
        _backtestSymbols = ResolveBacktestSymbols(config);
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
    /// Max parallelism：預設 1（序列）、clamp [1, 16]。
    /// 5/19 診斷確認：parallel>1 觸發 cache-protocol framing bug（multi-packet frame 處理
    /// 在並發 dispatch 下解析錯位、worker 收到 "Invalid magic bytes" 主動斷連）。1 strategy
    /// 0 errors / 24 strategies × parallel=4 → 286/288 fail = clean signal。
    ///
    /// 預估 24 條 × N watches × 3 tf 序列跑 30-60 秒、24h auto batch 完全可接受。
    /// 待 cache-protocol layer 修好後才能恢復並行（拉 default 回 4）。
    /// </summary>
    internal static int ResolveMaxParallel(IConfiguration config)
        => Math.Clamp(config.GetValue("Lab:MaxParallel", 5), 1, 16);  // 6-core VPS：留 1 核給 broker/系統

    /// <summary>
    /// 回測 universe（BingX 格式 symbol）。預設 = 深度回補的 22 個流動性主流幣;跟即時交易 watch 解耦、純分析。
    /// `Lab:BacktestSymbols` 可覆寫（逗號分隔）。空字串 → 回空陣列（只跑 watch list）。
    /// </summary>
    internal static string[] ResolveBacktestSymbols(IConfiguration config)
    {
        const string def = "BTC-USDT,ETH-USDT,SOL-USDT,BNB-USDT,XRP-USDT,ADA-USDT,AVAX-USDT,DOGE-USDT," +
                           "DOT-USDT,LINK-USDT,LTC-USDT,TRX-USDT,ATOM-USDT,UNI-USDT,NEAR-USDT,APT-USDT," +
                           "ARB-USDT,OP-USDT,INJ-USDT,SUI-USDT,FIL-USDT,TIA-USDT";
        var raw = config.GetValue("Lab:BacktestSymbols", def) ?? def;
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// B：per-regime ranking。每 (symbol, timeframe, regime) 找 score 最高的標 Recommended=true。
    /// （之前 per (symbol, timeframe)、不分行情、震盪市贏家會壓過趨勢市贏家）
    ///
    /// 過濾條件：
    ///   1. 沒 error 且 Trades >= minTrades（樣本不足拒推薦、防 lucky 1-trade）
    ///   2. WfFolds>0 且 |IsOosGap|>=0.7 → 排除（過擬合紅線）
    ///   3. WfFolds=0（沒跑成功）→ 不擋（向後相容、bars 不夠也能用 IS-only ranking）
    ///
    /// 同 run 內 ClassifyRegime 對每 (symbol, tf) 跑一次共用、所以單 run 仍每 (symbol, tf)
    /// 一個 recommended；跨 run 不同行情會逐步累積各 regime 的贏家。
    /// In-place mutate input list（避免 copy 大量 entry）。
    /// </summary>
    internal static void MarkRecommendedPerRegime(List<BacktestResultEntry> results, int minTrades)
    {
        var grouped = results
            .Where(r => string.IsNullOrEmpty(r.Error) && r.Trades >= minTrades)
            .Where(r => r.WfFolds == 0 || Math.Abs(r.IsOosGap) < 0.7m)
            .GroupBy(r => (r.Symbol, r.Timeframe, Regime: r.Regime ?? "unknown"));
        foreach (var g in grouped)
        {
            var best = g.OrderByDescending(r => r.Score).FirstOrDefault();
            if (best != null) best.Recommended = true;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("ScheduledBacktest service started, interval={Hours}h, runOnStart={RunOnStart}",
            _interval.TotalHours, _runOnStart);

        if (_runOnStart)
        {
            // workers 連到 broker 是 broker 啟動後幾秒~幾十秒（backoff）才完成；runOnStart 立刻跑會撞到
            // "worker offline" 被 skip。先等兩個必要 worker 上線（最多 90s）再跑。
            await WaitForWorkersAsync(TimeSpan.FromSeconds(90), ct);
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

    /// <summary>等 strategy.signal + quote.ohlcv 兩個 worker 上線（最多 timeout）；給 runOnStart 用、避免撞 reconnect 窗。</summary>
    private async Task WaitForWorkersAsync(TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            if (_registry.HasAvailableWorker("strategy.signal") && _registry.HasAvailableWorker("quote.ohlcv"))
                return;
            try { await Task.Delay(TimeSpan.FromSeconds(3), ct); }
            catch (TaskCanceledException) { return; }
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

        // 回測 universe = perp watches（mode=perp_*）∪ 設定的分析清單（_backtestSymbols、跟即時交易解耦）。
        // watch 帶原本 owner;純分析 symbol 用 prn_dashboard / bingx。同 symbol 以 watch 優先（保留 owner）。
        var targets = _autoTrader.WatchList.Values
            .Where(w => w.Mode == "perp_long_only" || w.Mode == "perp_both")
            .Select(w => (
                Symbol: w.Symbol,
                Exchange: string.IsNullOrEmpty(w.Exchange) ? "bingx" : w.Exchange,
                Owner: string.IsNullOrEmpty(w.OwnerPrincipalId) ? "prn_dashboard" : w.OwnerPrincipalId))
            .ToList();
        var seenSymbols = new HashSet<string>(targets.Select(t => t.Symbol), StringComparer.OrdinalIgnoreCase);
        foreach (var sym in _backtestSymbols)
            if (seenSymbols.Add(sym))
                targets.Add((sym, "bingx", "prn_dashboard"));

        if (targets.Count == 0)
        {
            run.FinishedAt = DateTime.UtcNow;
            run.Notes = "No backtest targets (no watches, no Lab:BacktestSymbols)";
            _db.Update(run);
            _logger.LogInformation("Backtest run {RunId}: no targets, skipped", runId);
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

        // Phase 1：每 (target, tf) 抓一次 bars（順序、避免一次灌爆 quote-worker）。bars 算一次 regime、
        // 快取給該 symbol-tf 的所有策略共用。不足的直接記 error、不進 job 清單。
        var jobs = new List<(string Symbol, string Exchange, string Owner, string Tf, JsonElement Bars, string Regime)>();
        foreach (var t in targets)
        {
            foreach (var tf in Timeframes)
            {
                var barsOpt = await FetchBarsAsync(t.Symbol, tf, BarsPerBacktest, traceId, ct);
                var barsCount = barsOpt.HasValue ? barsOpt.Value.GetArrayLength() : 0;
                if (!barsOpt.HasValue || barsCount < 50)
                {
                    foreach (var strat in _strategies)
                        allResults.Add(new BacktestResultEntry
                        {
                            RunId = runId, Symbol = t.Symbol, Exchange = t.Exchange,
                            Timeframe = tf, Strategy = strat, BarsCount = barsCount,
                            Error = "insufficient bars",
                        });
                    continue;
                }
                jobs.Add((t.Symbol, t.Exchange, t.Owner, tf, barsOpt.Value, ClassifyRegime(barsOpt.Value)));
            }
        }

        // Phase 2：每 symbol-tf 一次 batch dispatch —— bars 只送一次、worker 端用 Parallel.ForEach 跨核心
        // 一次跑完該 symbol-tf 的全部策略。取代舊的「每策略各派一次（同份 1000-bar payload 重複序列化 N 次）」，
        // 消掉 broker 序列化瓶頸 + round-trip 從 N×symbol-tf 降到 symbol-tf。外層維持順序（一個 batch 已吃滿 worker）。
        foreach (var j in jobs)
        {
            if (ct.IsCancellationRequested) break;
            var entries = await RunBatchAsync(runId, j.Symbol, j.Exchange, j.Owner, j.Tf, j.Bars, j.Regime, traceId, ct);
            allResults.AddRange(entries);
            errors += entries.Count(e => !string.IsNullOrEmpty(e.Error));
        }

        MarkRecommendedPerRegime(allResults, _minTrades);

        // 全部寫入
        foreach (var r in allResults) _db.Insert(r);

        run.FinishedAt = DateTime.UtcNow;
        run.DurationMs = (long)(run.FinishedAt.Value - run.StartedAt).TotalMilliseconds;
        run.SymbolsCount = targets.Count;
        run.ResultsCount = allResults.Count;
        run.ErrorCount = errors;
        _db.Update(run);

        _logger.LogInformation("Backtest run {RunId} done: {Symbols} symbols × {Tf} tf × {Strat} strat = {Total} results, {Errors} errors, {Ms}ms",
            runId, targets.Count, Timeframes.Length, _strategies.Length, allResults.Count, errors, run.DurationMs);
        return runId;
    }

    private async Task<JsonElement?> FetchBarsAsync(string symbol, string interval, int limit, string traceId, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { symbol, interval, limit });
        // get_bars_funding：OHLCV + 資金費率 as-of join,每根 bar 帶 funding_rate(無 perp 資料則 null)。
        // 讓 character_ensemble 等的 funding 閘門在批次回測裡真的吃得到資料（沒有就自動降級）。
        var req = new ApprovedRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            CapabilityId = "quote.ohlcv", Route = "get_bars_funding", Payload = payload,
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

    /// <summary>
    /// 一次對某 (symbol, tf) 派發 backtest_batch：bars 送一次、worker 端跨核心跑全部 _strategies。
    /// 回每個策略的 entry（含 OOS + score）。整批 dispatch 失敗 → 全部標 error。
    /// </summary>
    private async Task<List<BacktestResultEntry>> RunBatchAsync(
        string runId, string symbol, string exchange, string owner, string tf,
        JsonElement bars, string regime, string traceId, CancellationToken ct)
    {
        var list = new List<BacktestResultEntry>();
        var barsCount = bars.GetArrayLength();

        var payload = JsonSerializer.Serialize(new
        {
            symbol, exchange, interval = tf, bars,
            strategies = _strategies,
            initial_cash = 1000.0,
            train_bars = 365, test_bars = 90, stride = 90,
        });
        var req = new ApprovedRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            CapabilityId = "strategy.signal", Route = "backtest_batch", Payload = payload,
            Scope = "{}", PrincipalId = "system",
            TaskId = "scheduled-backtest", SessionId = "scheduled-backtest",
            TraceId = traceId,
        };
        var result = await _dispatcher.DispatchAsync(req);

        BacktestResultEntry New(string strat) => new()
        {
            RunId = runId, Symbol = symbol, Exchange = exchange, Timeframe = tf,
            Strategy = strat, Regime = regime, OwnerPrincipalId = owner, BarsCount = barsCount,
        };

        if (!result.Success)
        {
            var err = result.ErrorMessage?.Length > 200 ? result.ErrorMessage[..200] : result.ErrorMessage;
            foreach (var s in _strategies) { var e = New(s); e.Error = err; list.Add(e); }
            return list;
        }

        try
        {
            var doc = JsonDocument.Parse(result.ResultPayload ?? "{}").RootElement;
            if (doc.TryGetProperty("bars_count", out var bc) && bc.ValueKind == JsonValueKind.Number)
                barsCount = bc.GetInt32();
            if (doc.TryGetProperty("results", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in arr.EnumerateArray())
                {
                    var name = r.TryGetProperty("strategy", out var sn) ? sn.GetString() ?? "" : "";
                    var entry = New(name);
                    entry.BarsCount = barsCount;
                    if (r.TryGetProperty("error", out var er) && er.ValueKind == JsonValueKind.String)
                    {
                        var em = er.GetString();
                        entry.Error = em?.Length > 200 ? em[..200] : em;
                    }
                    else
                    {
                        entry.TotalReturnPct = GetDec(r, "total_return_pct");
                        entry.Sharpe         = GetDec(r, "sharpe_ratio");
                        entry.WinRate        = GetDec(r, "win_rate");
                        entry.MaxDdPct       = GetDec(r, "max_drawdown_pct");
                        entry.Trades         = r.TryGetProperty("total_trades", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : 0;
                        entry.WfFolds        = r.TryGetProperty("wf_folds", out var wf) && wf.ValueKind == JsonValueKind.Number ? wf.GetInt32() : 0;
                        entry.OosReturnPct   = GetDec(r, "oos_return_pct");
                        entry.OosSharpe      = GetDec(r, "oos_sharpe");
                        entry.OosWinRate     = GetDec(r, "oos_win_rate");
                        entry.IsOosGap       = GetDec(r, "is_oos_gap");
                        entry.Score          = ComputeScore(entry, _scoreWeights);
                    }
                    list.Add(entry);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Batch result parse failed for {Sym}/{Tf}", symbol, tf);
            if (list.Count == 0)
                foreach (var s in _strategies) { var e = New(s); e.Error = "batch parse failed"; list.Add(e); }
        }
        return list;
    }

    private static decimal GetDec(JsonElement el, string key)
        => el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : 0m;

    /// <summary>
    /// composite ranking: Sharpe·sharpe_norm + Return·return_norm + WinRate·win_rate_norm
    ///                  − DrawdownPenalty·dd_penalty + Oos·oos_quality
    /// 預設 ScoreWeights = (Sharpe 0.30, Return 0.40, WinRate 0.05, DdPenalty 0.1, Oos 0.15)。
    /// 想 tune：appsettings.json `Lab:Score:Return = 0.5` 等個別覆寫、不用改 code。
    ///
    /// OOS-first：walk-forward 有跑（WfFolds>0）就用「OOS 報酬 / sharpe / 勝率」排名 —— 真正的樣本外
    /// 表現,而不是被過擬合灌水的 in-sample。沒跑（bars 不夠）才退回 IS。另對 IS-OOS gap 加 overfit 懲罰。
    /// 刻意降 WinRate 權重：高勝率小賺（均值回歸）不該壓過低勝率大賺（趨勢）。
    /// 沒交易直接 0 分（拒列入 recommended）。internal static 給 unit test 用。
    /// </summary>
    internal static decimal ComputeScore(BacktestResultEntry r, ScoreWeights w)
    {
        if (r.Trades == 0) return 0m;

        // walk-forward 有跑就用 OOS 指標（真樣本外）；否則退回 IS。
        var hasOos = r.WfFolds > 0;
        var ret    = hasOos ? r.OosReturnPct : r.TotalReturnPct;
        var sharpe = hasOos ? r.OosSharpe    : r.Sharpe;
        var win    = hasOos ? r.OosWinRate   : r.WinRate;

        // 報酬 /20 正規化（±20% 打滿、允許負值反映虧損）；OOS 報酬通常個位數、這個尺度才有鑑別力。
        var returnNorm  = Math.Clamp(ret / 20m, -1m, 1m);
        var sharpeNorm  = Math.Clamp((sharpe + 2m) / 7m, 0m, 1m);
        var winRateNorm = Math.Clamp(win / 100m, 0m, 1m);
        var ddPenalty   = Math.Clamp(r.MaxDdPct / 50m, 0m, 1m);
        // overfit 懲罰：IS-OOS gap 是百分點、/10 正規化（10pp gap = 滿罰）。沒跑 OOS → 不罰。
        var overfitPenalty = hasOos ? Math.Clamp(Math.Abs(r.IsOosGap) / 10m, 0m, 1m) : 0m;

        var score = w.Return * returnNorm
                  + w.Sharpe * sharpeNorm
                  + w.WinRate * winRateNorm
                  - w.DrawdownPenalty * ddPenalty
                  - w.Oos * overfitPenalty;

        return Math.Round(score, 4);
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
