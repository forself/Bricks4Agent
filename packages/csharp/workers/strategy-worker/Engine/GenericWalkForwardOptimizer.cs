using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 通用 walk-forward 參數優化器 —— 任何「有 ParamSchema 且會從 config.Params 讀參數」的策略都能掃。
///
/// 流程（anchored）：每個 window 在 train 段對 ParamSchema 的格點做 grid search 找最佳參數（by train Sharpe）、
/// 再用該參數在 test 段（OOS）評估。同時跑一條「預設參數」baseline 做對照。
///
/// 回答的核心問題：**調參到底能不能把簡單策略的 OOS 從負救成正？**
///   - opt OOS > def OOS 且轉正 → 調參有救（找到 edge）
///   - opt OOS 仍 ≈ def OOS 或負 → 純 curve-fit、沒救（強證據:該策略無 OOS edge）
/// </summary>
public static class GenericWalkForwardOptimizer
{
    public class Result
    {
        public string Strategy { get; set; } = "";
        public string Symbol { get; set; } = "";
        public int WindowCount { get; set; }
        public int TrainBars { get; set; }
        public int TestBars { get; set; }
        public int GridSize { get; set; }

        // 優化後（每 window 重選最佳參數、評 OOS）
        public decimal OptOosReturnPct { get; set; }  // 串接複利
        public decimal OptOosSharpe { get; set; }      // 平均
        public decimal OptOosWinRate { get; set; }

        // 預設參數 baseline（同樣 OOS 評估、不調參）
        public decimal DefOosReturnPct { get; set; }
        public decimal DefOosSharpe { get; set; }

        public Dictionary<string, string> MostCommonBestParams { get; set; } = new();
        public List<WindowResult> Windows { get; set; } = new();
        public string? Error { get; set; }
    }

    public class WindowResult
    {
        public int Index { get; set; }
        public Dictionary<string, object> BestParams { get; set; } = new();
        public decimal TrainSharpe { get; set; }
        public decimal OosReturnPct { get; set; }
        public decimal OosSharpe { get; set; }
    }

    private const int MaxGrid = 400;  // 防參數空間爆炸

    public static Result Optimize(
        IStrategy strategy, List<BarData> bars, StrategyConfig baseConfig,
        int trainBars, int testBars, decimal cash = 1000m, decimal commission = 0.001m)
    {
        var r = new Result
        {
            Strategy = strategy.Name, Symbol = baseConfig.Symbol,
            TrainBars = trainBars, TestBars = testBars,
        };

        if (strategy.ParamSchema.Count == 0) { r.Error = "strategy has no tunable ParamSchema"; return r; }
        var grid = BuildGrid(strategy.ParamSchema);
        r.GridSize = grid.Count;
        if (grid.Count > MaxGrid) { r.Error = $"grid too large ({grid.Count} > {MaxGrid})"; return r; }
        if (bars.Count < trainBars + testBars) { r.Error = "not enough bars for one window"; return r; }

        var paramHits = new Dictionary<string, Dictionary<string, int>>();  // key -> value -> count
        var defReturns = new List<decimal>();
        var defSharpes = new List<decimal>();
        var trainEnd = trainBars;

        while (trainEnd + testBars <= bars.Count)
        {
            var trainSeg = bars.Take(trainEnd).ToList();
            var fullForOos = bars.Take(trainEnd + testBars).ToList();
            var testStart = bars[trainEnd].OpenTime;

            // 1. train 上 grid search 找最佳參數（by train Sharpe、需有交易）
            Dictionary<string, object>? best = null;
            decimal bestSharpe = decimal.MinValue;
            foreach (var combo in grid)
            {
                var bt = BacktestEngine.Run(strategy, trainSeg, WithParams(baseConfig, combo), cash, commission);
                if (bt.TotalTrades <= 0) continue;
                if (bt.SharpeRatio > bestSharpe) { bestSharpe = bt.SharpeRatio; best = combo; }
            }
            best ??= grid[0];
            if (bestSharpe == decimal.MinValue) bestSharpe = 0m;

            // 2. OOS：用最佳參數跑完整段、切出 test 區間
            var optFull = BacktestEngine.Run(strategy, fullForOos, WithParams(baseConfig, best), cash, commission);
            var optOos = WalkForwardOptimizer.ExtractOosMetrics(optFull, testStart);

            // 3. baseline：預設參數（不帶 Params）
            var defFull = BacktestEngine.Run(strategy, fullForOos, WithParams(baseConfig, null), cash, commission);
            var defOos = WalkForwardOptimizer.ExtractOosMetrics(defFull, testStart);

            r.Windows.Add(new WindowResult
            {
                Index = r.Windows.Count, BestParams = best,
                TrainSharpe = Math.Round(bestSharpe, 4),
                OosReturnPct = optOos.ReturnPct, OosSharpe = optOos.Sharpe,
            });

            foreach (var (k, v) in best)
            {
                var vs = v.ToString() ?? "";
                if (!paramHits.TryGetValue(k, out var m)) paramHits[k] = m = new();
                m[vs] = m.TryGetValue(vs, out var c) ? c + 1 : 1;
            }

            defReturns.Add(defOos.ReturnPct);
            defSharpes.Add(defOos.Sharpe);

            trainEnd += testBars;
        }

        r.WindowCount = r.Windows.Count;
        if (r.Windows.Count > 0)
        {
            decimal cum = 1m;
            foreach (var w in r.Windows) cum *= (1m + w.OosReturnPct / 100m);
            r.OptOosReturnPct = Math.Round((cum - 1m) * 100m, 4);
            r.OptOosSharpe = Math.Round(r.Windows.Average(w => w.OosSharpe), 4);

            decimal cumDef = 1m;
            foreach (var rr in defReturns) cumDef *= (1m + rr / 100m);
            r.DefOosReturnPct = Math.Round((cumDef - 1m) * 100m, 4);
            r.DefOosSharpe = defSharpes.Count > 0 ? Math.Round(defSharpes.Average(), 4) : 0m;

            foreach (var (k, m) in paramHits)
                r.MostCommonBestParams[k] = m.OrderByDescending(kv => kv.Value).First().Key;
        }
        return r;
    }

    private static StrategyConfig WithParams(StrategyConfig b, Dictionary<string, object>? p) => new()
    {
        Name = b.Name, Symbol = b.Symbol, Exchange = b.Exchange, Interval = b.Interval,
        SmaFast = b.SmaFast, SmaSlow = b.SmaSlow,
        RsiPeriod = b.RsiPeriod, RsiOversold = b.RsiOversold, RsiOverbought = b.RsiOverbought,
        MacdFast = b.MacdFast, MacdSlow = b.MacdSlow, MacdSignal = b.MacdSignal,
        BarLimit = b.BarLimit, HtfBars = b.HtfBars, HtfInterval = b.HtfInterval,
        Params = p,
    };

    /// <summary>從 ParamSchema 展開 cartesian 格點。Choices 優先;否則 Min..Max step Step。</summary>
    public static List<Dictionary<string, object>> BuildGrid(IReadOnlyDictionary<string, ParamSpec> schema)
    {
        var dims = new List<(string Key, List<object> Vals)>();
        foreach (var (key, spec) in schema)
        {
            var vals = new List<object>();
            if (spec.Choices is { Length: > 0 })
                vals.AddRange(spec.Choices);
            else if (spec.Min != null && spec.Max != null && spec.Step != null)
            {
                double min = Convert.ToDouble(spec.Min), max = Convert.ToDouble(spec.Max), step = Convert.ToDouble(spec.Step);
                if (step <= 0) step = 1;
                for (double v = min; v <= max + 1e-9; v += step)
                    vals.Add(spec.Type == "int" ? (int)Math.Round(v) : (decimal)v);
            }
            else vals.Add(spec.Default);
            if (vals.Count == 0) vals.Add(spec.Default);
            dims.Add((key, vals));
        }

        var grid = new List<Dictionary<string, object>> { new() };
        foreach (var (key, vals) in dims)
        {
            var next = new List<Dictionary<string, object>>();
            foreach (var combo in grid)
                foreach (var v in vals)
                    next.Add(new Dictionary<string, object>(combo) { [key] = v });
            grid = next;
        }
        return grid;
    }
}
