using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Services;
using FunctionPool.Registry;

namespace Broker.Services;

/// <summary>
/// 自主策略研究迴圈 (AI-autonomous strategy research loop)。
///
/// 對比 XQ 全球贏家的「策略市集」：他們是人類使用者上傳策略，其他人訂閱；
/// 我這個是 LLM 自己當研究員，整條研究週期無人介入：
///
///   1. StrategyGeneratorService   → LLM 提參數假設（含 rationale）
///   2. dispatcher → strategy-worker → BacktestEngine → 回測指標
///   3. Broker 這邊切 80/20 holdout 算 In-sample / Out-of-sample + degradation
///   4. 把結果塞回 StrategyGenerator 當 history，LLM 讀完提下一個假設
///   5. 跑 N 代直到預算耗盡；StrategyCandidateRepository 記整條血緣
///
/// 跟「LlmStrategy」的差別：LlmStrategy 是**inference-time**（交易時叫 LLM 給訊號），
/// 這個是**design-time**（研究時叫 LLM 設計策略）。同個 LLM Proxy，不同使用層。
/// </summary>
public class StrategyResearchLoopService
{
    private readonly StrategyGeneratorService _generator;
    private readonly StrategyCandidateRepository _repo;
    private readonly IExecutionDispatcher _dispatcher;
    private readonly IWorkerRegistry _registry;
    private readonly ILogger<StrategyResearchLoopService> _logger;

    // 參數
    private const int HoldoutBars = 60;     // 最後 N 根 K 線當 OOS；前面當 IS
    private const decimal InitialCash = 100_000m;

    public StrategyResearchLoopService(
        StrategyGeneratorService generator,
        StrategyCandidateRepository repo,
        IExecutionDispatcher dispatcher,
        IWorkerRegistry registry,
        ILogger<StrategyResearchLoopService> logger)
    {
        _generator = generator;
        _repo = repo;
        _dispatcher = dispatcher;
        _registry = registry;
        _logger = logger;
    }

    public bool IsEnabled =>
        _generator.IsEnabled
        && _registry.HasAvailableWorker("quote.ohlcv")
        && _registry.HasAvailableWorker("strategy.signal");

    public async Task<ResearchRun> RunAsync(
        string symbol,
        string family,
        int targetGenerations,
        int dataLimit,
        CancellationToken ct = default)
    {
        var run = _repo.StartRun(symbol, family, targetGenerations);
        if (!IsEnabled)
        {
            _repo.CompleteRun(run.RunId, "failed", "LLM 或 worker 不可用");
            return run;
        }

        // 只抓一次 K 線，全程重用
        var barsJson = await FetchBarsAsync(symbol, dataLimit, ct);
        if (string.IsNullOrEmpty(barsJson))
        {
            _repo.CompleteRun(run.RunId, "failed", $"quote-worker 沒有 {symbol} 的 K 線資料");
            return run;
        }

        for (int gen = 0; gen < targetGenerations && !ct.IsCancellationRequested; gen++)
        {
            var history = BuildHistory(run);

            // 1. LLM 生成候選參數
            var generation = await _generator.GenerateAsync(family, symbol, history, ct);

            var candidate = new StrategyCandidate
            {
                Family = family,
                ParentIndex = history.Count > 0 ? run.Candidates[history.Count - 1].Index : (int?)null,
                Parameters = generation.Parameters,
                Rationale = generation.Rationale,
                LlmModel = generation.Model,
            };

            if (!generation.Success)
            {
                candidate.BacktestError = generation.Error;
                _repo.AddCandidate(run.RunId, candidate);
                _logger.LogWarning("Gen {Gen} LLM failed: {Err}", gen, generation.Error);
                continue;
            }

            // 2. 在 broker 側做 80/20 holdout 回測
            try
            {
                var (isMetrics, oosMetrics) = await EvaluateCandidateAsync(family, symbol, candidate.Parameters, barsJson, ct);

                candidate.BacktestSuccess = true;
                candidate.InSampleSharpe = isMetrics.Sharpe;
                candidate.InSampleReturnPct = isMetrics.ReturnPct;
                candidate.InSampleMaxDrawdownPct = isMetrics.MaxDrawdownPct;
                candidate.InSampleWinRate = isMetrics.WinRate;
                candidate.InSampleTrades = isMetrics.Trades;

                candidate.OutOfSampleSharpe = oosMetrics.Sharpe;
                candidate.ReturnPct = oosMetrics.ReturnPct;
                candidate.MaxDrawdownPct = oosMetrics.MaxDrawdownPct;
                candidate.WinRate = oosMetrics.WinRate;
                candidate.Trades = oosMetrics.Trades;
                candidate.DegradationRatio = isMetrics.Sharpe == 0m
                    ? 0m
                    : Math.Round(oosMetrics.Sharpe / isMetrics.Sharpe, 4);
            }
            catch (Exception ex)
            {
                candidate.BacktestSuccess = false;
                candidate.BacktestError = ex.Message;
                _logger.LogWarning(ex, "Gen {Gen} backtest failed", gen);
            }

            _repo.AddCandidate(run.RunId, candidate);
            _logger.LogInformation(
                "Gen {Gen} params={Params} IS Sharpe={IS:F2} OOS Sharpe={OOS:F2}",
                gen, string.Join(",", candidate.Parameters.Select(kv => $"{kv.Key}={kv.Value}")),
                candidate.InSampleSharpe, candidate.OutOfSampleSharpe);
        }

        _repo.CompleteRun(run.RunId, "completed");
        return run;
    }

    // ── 評估單個候選：80/20 holdout ─────────────────────────────────

    private async Task<(BacktestMetrics inSample, BacktestMetrics outOfSample)> EvaluateCandidateAsync(
        string family, string symbol, Dictionary<string, int> parameters, string barsJson, CancellationToken ct)
    {
        using var barsDoc = JsonDocument.Parse(barsJson);
        var allBars = barsDoc.RootElement.GetProperty("bars");
        var barCount = allBars.GetArrayLength();
        if (barCount < HoldoutBars + 30)
            throw new InvalidOperationException($"Bars too few: {barCount} (need >= {HoldoutBars + 30})");

        // IS = 前 (total - HoldoutBars)；OOS = 最後 HoldoutBars 根但回測時 warmup 用所有前面的歷史
        var splitIndex = barCount - HoldoutBars;

        // 用整段 bars 做一次完整回測 → 取 IS 指標（到 splitIndex 為止）和 OOS 指標（splitIndex 之後）
        var fullResult = await RunBacktestAsync(family, symbol, allBars, parameters, ct);
        if (fullResult == null)
            throw new InvalidOperationException("full backtest failed");

        var splitDate = ParseDate(allBars[splitIndex], "open_time");

        var isMetrics = ExtractMetricsBefore(fullResult, splitDate);
        var oosMetrics = ExtractMetricsAfter(fullResult, splitDate);

        return (isMetrics, oosMetrics);
    }

    private async Task<JsonDocument?> RunBacktestAsync(
        string family, string symbol, JsonElement bars, Dictionary<string, int> parameters, CancellationToken ct)
    {
        // 組 payload
        var payloadObj = new Dictionary<string, object>
        {
            ["strategy"] = family,
            ["symbol"] = symbol,
            ["bars"] = JsonSerializer.Deserialize<JsonElement>(bars.GetRawText()),
            ["initial_cash"] = InitialCash,
        };
        foreach (var kv in parameters) payloadObj[kv.Key] = kv.Value;

        var req = BuildReq("strategy.signal", "backtest", JsonSerializer.Serialize(payloadObj));
        var r = await _dispatcher.DispatchAsync(req);
        if (!r.Success || string.IsNullOrEmpty(r.ResultPayload)) return null;
        return JsonDocument.Parse(r.ResultPayload);
    }

    // ── 從 BacktestResult 切出某區段指標 ──────────────────────────────

    private BacktestMetrics ExtractMetricsBefore(JsonDocument doc, DateTime cutoff)
        => ExtractWindowMetrics(doc, before: cutoff);

    private BacktestMetrics ExtractMetricsAfter(JsonDocument doc, DateTime cutoff)
        => ExtractWindowMetrics(doc, after: cutoff);

    private BacktestMetrics ExtractWindowMetrics(JsonDocument doc, DateTime? before = null, DateTime? after = null)
    {
        var m = new BacktestMetrics();
        var root = doc.RootElement;

        if (!root.TryGetProperty("equity_curve", out var curveEl) || curveEl.ValueKind != JsonValueKind.Array)
            return m;

        var points = new List<(DateTime date, decimal value)>();
        foreach (var p in curveEl.EnumerateArray())
        {
            var d = ParseDate(p, "date");
            var v = GetDec(p, "value");
            points.Add((d, v));
        }

        if (points.Count < 2) return m;

        List<(DateTime date, decimal value)> windowPts;
        decimal? startEquity;
        if (before.HasValue)
        {
            windowPts = points.Where(p => p.date < before.Value).ToList();
            startEquity = points[0].value;  // 初始本金
        }
        else
        {
            windowPts = points.Where(p => p.date >= after!.Value).ToList();
            var lastBeforeCutoff = points.Where(p => p.date < after!.Value).LastOrDefault();
            startEquity = lastBeforeCutoff.value > 0 ? lastBeforeCutoff.value : InitialCash;
        }

        if (windowPts.Count == 0) return m;

        var endEquity = windowPts.Last().value;
        if (startEquity.HasValue && startEquity.Value > 0)
            m.ReturnPct = Math.Round((endEquity - startEquity.Value) / startEquity.Value * 100m, 4);

        // MaxDD
        decimal peak = startEquity ?? windowPts[0].value;
        decimal maxDD = 0m;
        foreach (var p in windowPts)
        {
            if (p.value > peak) peak = p.value;
            if (peak > 0 && (peak - p.value) / peak > maxDD)
                maxDD = (peak - p.value) / peak;
        }
        m.MaxDrawdownPct = Math.Round(maxDD * 100m, 4);

        // Sharpe（從 daily return 年化）
        var combined = new List<decimal>();
        if (startEquity.HasValue) combined.Add(startEquity.Value);
        combined.AddRange(windowPts.Select(p => p.value));
        var returns = new List<decimal>();
        for (int i = 1; i < combined.Count; i++)
            if (combined[i - 1] > 0)
                returns.Add((combined[i] - combined[i - 1]) / combined[i - 1]);

        if (returns.Count >= 2)
        {
            var mean = returns.Average();
            var variance = returns.Sum(r => (r - mean) * (r - mean)) / (returns.Count - 1);
            var stdDev = (decimal)Math.Sqrt((double)variance);
            if (stdDev > 0)
                m.Sharpe = Math.Round(mean / stdDev * (decimal)Math.Sqrt(252.0), 4);
        }

        // Trades + WinRate
        if (root.TryGetProperty("trades", out var tradesEl) && tradesEl.ValueKind == JsonValueKind.Array)
        {
            var tradesInWindow = new List<(DateTime entry, decimal pnl)>();
            foreach (var t in tradesEl.EnumerateArray())
            {
                var entry = ParseDate(t, "entry_date");
                var pnl = GetDec(t, "pnl");
                bool include = before.HasValue ? entry < before.Value : entry >= after!.Value;
                if (include) tradesInWindow.Add((entry, pnl));
            }
            m.Trades = tradesInWindow.Count;
            if (m.Trades > 0)
                m.WinRate = Math.Round((decimal)tradesInWindow.Count(t => t.pnl > 0) / m.Trades, 4);
        }

        return m;
    }

    // ── 建立給 LLM 看的歷史摘要 ──────────────────────────────────────

    private List<PriorAttempt> BuildHistory(ResearchRun run)
    {
        return run.Candidates
            .Where(c => c.BacktestSuccess)
            .Select(c => new PriorAttempt
            {
                Parameters = new Dictionary<string, int>(c.Parameters),
                OutOfSampleSharpe = c.OutOfSampleSharpe,
                ReturnPct = c.ReturnPct,
                MaxDrawdownPct = c.MaxDrawdownPct,
                Trades = c.Trades,
            })
            .ToList();
    }

    private async Task<string?> FetchBarsAsync(string symbol, int limit, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { symbol, limit });
        var r = await _dispatcher.DispatchAsync(BuildReq("quote.ohlcv", "get_bars", payload));
        return r.Success ? r.ResultPayload : null;
    }

    // ── 輔助 ─────────────────────────────────────────────────────────

    private static ApprovedRequest BuildReq(string cap, string route, string payload = "{}")
        => new()
        {
            RequestId = Guid.NewGuid().ToString("N"),
            CapabilityId = cap,
            Route = route,
            Payload = payload,
            Scope = "{}",
            PrincipalId = "system",
            TaskId = "research-loop",
            SessionId = "research-loop",
        };

    private static decimal GetDec(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return 0m;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) return d;
        return 0m;
    }

    private static DateTime ParseDate(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return DateTime.MinValue;
        if (v.ValueKind == JsonValueKind.String && DateTime.TryParse(v.GetString(), out var dt))
            return dt.ToUniversalTime();
        return DateTime.MinValue;
    }

    private class BacktestMetrics
    {
        public decimal Sharpe { get; set; }
        public decimal ReturnPct { get; set; }
        public decimal MaxDrawdownPct { get; set; }
        public decimal WinRate { get; set; }
        public int Trades { get; set; }
    }
}
