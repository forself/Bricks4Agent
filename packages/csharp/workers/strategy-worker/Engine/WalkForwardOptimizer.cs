using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// Walk-Forward 優化器 — 解決 ParameterOptimizer 的 in-sample overfit 問題。
///
/// 流程（Anchored Walk-Forward）：
///   bars = [0 ................... N]
///
///   Window 1:  train = bars[0          : trainEnd1]  → 找最佳參數 P1
///              test  = bars[trainEnd1  : testEnd1 ]  → 用 P1 回測 → OOS 績效
///
///   Window 2:  train = bars[0          : trainEnd2]  → 找最佳參數 P2（通常 != P1）
///              test  = bars[trainEnd2  : testEnd2 ]  → 用 P2 回測 → OOS 績效
///   ...
///
/// 彙總所有 test 區間績效 = **真正的 out-of-sample 績效**。
///
/// 關鍵指標：degradation_ratio = OOS_Sharpe / IS_Sharpe
///   → 接近 1：策略穩健、參數不是過擬合
///   → 遠小於 1（或負）：IS 看起來很美、實戰垃圾
/// </summary>
public static class WalkForwardOptimizer
{
    public class WalkForwardResult
    {
        public string Strategy { get; set; } = "";
        public string Symbol { get; set; } = "";
        public int TotalBars { get; set; }
        public int WindowCount { get; set; }
        public int InitialTrainBars { get; set; }
        public int TestBars { get; set; }

        // In-sample / Out-of-sample 對比
        public decimal AvgInSampleSharpe { get; set; }
        public decimal AvgOutOfSampleSharpe { get; set; }
        public decimal DegradationRatio { get; set; }       // OOS / IS；接近 1 = 穩健
        public decimal AggregateOosReturnPct { get; set; }  // 串接所有 test 區間的報酬
        public decimal AggregateOosWinRate { get; set; }

        public List<Window> Windows { get; set; } = new();
    }

    public class Window
    {
        public int Index { get; set; }
        public int TrainFrom { get; set; }
        public int TrainTo { get; set; }
        public int TestFrom { get; set; }
        public int TestTo { get; set; }
        public DateTime TrainStartDate { get; set; }
        public DateTime TestStartDate { get; set; }
        public DateTime TestEndDate { get; set; }

        public Dictionary<string, int> BestParams { get; set; } = new();
        public decimal InSampleSharpe { get; set; }
        public decimal InSampleReturnPct { get; set; }

        public decimal OutOfSampleSharpe { get; set; }
        public decimal OutOfSampleReturnPct { get; set; }
        public decimal OutOfSampleWinRate { get; set; }
        public decimal OutOfSampleMaxDrawdownPct { get; set; }
        public int OutOfSampleTrades { get; set; }
    }

    /// <summary>
    /// SMA Cross 策略的 walk-forward 優化。
    /// </summary>
    public static WalkForwardResult RunSma(
        List<BarData> bars,
        StrategyConfig baseConfig,
        int initialTrainBars = 200,
        int testBars = 50,
        decimal cash = 100_000m)
    {
        return Run(bars, baseConfig, "sma_cross", initialTrainBars, testBars, cash,
            trainingBars => ParameterOptimizer.OptimizeSma(trainingBars, baseConfig, cash),
            bestParams => new SmaCrossStrategy(),
            bestParams => ApplySmaParams(baseConfig, bestParams));
    }

    /// <summary>
    /// RSI 策略的 walk-forward 優化。
    /// </summary>
    public static WalkForwardResult RunRsi(
        List<BarData> bars,
        StrategyConfig baseConfig,
        int initialTrainBars = 200,
        int testBars = 50,
        decimal cash = 100_000m)
    {
        return Run(bars, baseConfig, "rsi_oversold", initialTrainBars, testBars, cash,
            trainingBars => ParameterOptimizer.OptimizeRsi(trainingBars, baseConfig, cash),
            bestParams => new RsiStrategy(),
            bestParams => ApplyRsiParams(baseConfig, bestParams));
    }

    // ── 核心 Anchored walk-forward 流程 ─────────────────────────────────

    private static WalkForwardResult Run(
        List<BarData> bars,
        StrategyConfig baseConfig,
        string strategyName,
        int initialTrainBars,
        int testBars,
        decimal cash,
        Func<List<BarData>, ParameterOptimizer.OptimizeResult> optimize,
        Func<Dictionary<string, int>, IStrategy> buildStrategy,
        Func<Dictionary<string, int>, StrategyConfig> buildConfig)
    {
        var result = new WalkForwardResult
        {
            Strategy = strategyName,
            Symbol = baseConfig.Symbol,
            TotalBars = bars.Count,
            InitialTrainBars = initialTrainBars,
            TestBars = testBars,
        };

        if (bars.Count < initialTrainBars + testBars)
            return result;  // 資料不夠跑一個 window

        var trainEnd = initialTrainBars;
        var windowIdx = 0;

        while (trainEnd + testBars <= bars.Count)
        {
            var trainingBars = bars.Take(trainEnd).ToList();
            // 關鍵：OOS 回測餵**完整歷史 + 測試窗口**，讓指標有足夠 warm-up；
            // 事後用 testStartDate 當邊界過濾出真正的 OOS 交易與權益曲線
            var fullBarsForOos = bars.Take(trainEnd + testBars).ToList();
            var testStartDate = bars[trainEnd].OpenTime;

            // 1. In-sample 優化：在 training 區間找最佳參數
            var optResult = optimize(trainingBars);
            var bestParams = optResult.BestParams;

            // 2. Out-of-sample 驗證：用該參數跑整段歷史，再切出 test 區間的績效
            var testConfig = buildConfig(bestParams);
            var strategy = buildStrategy(bestParams);
            var fullBt = BacktestEngine.Run(strategy, fullBarsForOos, testConfig, cash);
            var oos = ExtractOosMetrics(fullBt, testStartDate);

            result.Windows.Add(new Window
            {
                Index = windowIdx,
                TrainFrom = 0,
                TrainTo = trainEnd,
                TestFrom = trainEnd,
                TestTo = trainEnd + testBars,
                TrainStartDate = trainingBars.First().OpenTime,
                TestStartDate = testStartDate,
                TestEndDate = bars[trainEnd + testBars - 1].OpenTime,
                BestParams = bestParams,
                InSampleSharpe = optResult.BestSharpe,
                InSampleReturnPct = optResult.BestReturn,
                OutOfSampleSharpe = oos.Sharpe,
                OutOfSampleReturnPct = oos.ReturnPct,
                OutOfSampleWinRate = oos.WinRate,
                OutOfSampleMaxDrawdownPct = oos.MaxDrawdownPct,
                OutOfSampleTrades = oos.Trades,
            });

            trainEnd += testBars;  // anchored：train 從 0 開始延長
            windowIdx++;
        }

        result.WindowCount = result.Windows.Count;
        if (result.Windows.Count > 0)
        {
            result.AvgInSampleSharpe = Math.Round(result.Windows.Average(w => w.InSampleSharpe), 4);
            result.AvgOutOfSampleSharpe = Math.Round(result.Windows.Average(w => w.OutOfSampleSharpe), 4);
            result.DegradationRatio = result.AvgInSampleSharpe == 0m
                ? 0m
                : Math.Round(result.AvgOutOfSampleSharpe / result.AvgInSampleSharpe, 4);

            // 串接 OOS 區間：算總報酬（複利）和平均勝率
            decimal cumReturn = 1m;
            foreach (var w in result.Windows)
                cumReturn *= (1m + w.OutOfSampleReturnPct / 100m);
            result.AggregateOosReturnPct = Math.Round((cumReturn - 1m) * 100m, 4);

            var totalTrades = result.Windows.Sum(w => w.OutOfSampleTrades);
            result.AggregateOosWinRate = totalTrades == 0
                ? 0m
                : Math.Round(
                    result.Windows.Sum(w => w.OutOfSampleWinRate * w.OutOfSampleTrades) / totalTrades,
                    4);
        }

        return result;
    }

    // ── 從完整 backtest 結果切出 OOS 區段的指標 ──────────────────────

    private class OosMetrics
    {
        public decimal Sharpe { get; set; }
        public decimal ReturnPct { get; set; }
        public decimal WinRate { get; set; }
        public decimal MaxDrawdownPct { get; set; }
        public int Trades { get; set; }
    }

    private static OosMetrics ExtractOosMetrics(BacktestEngine.BacktestResult full, DateTime testStart)
    {
        var m = new OosMetrics();

        // 1. 權益曲線的起點：test 前最後一筆；終點：最後一筆
        var curveBeforeTest = full.EquityCurve.Where(e => e.Date < testStart).ToList();
        var curveInTest = full.EquityCurve.Where(e => e.Date >= testStart).ToList();
        if (curveInTest.Count == 0) return m;

        var startEquity = curveBeforeTest.Count > 0 ? curveBeforeTest.Last().Value : full.InitialCash;
        var endEquity = curveInTest.Last().Value;
        m.ReturnPct = startEquity == 0 ? 0m : Math.Round((endEquity - startEquity) / startEquity * 100m, 4);

        // 2. MaxDD：peak-to-trough 在 test 區間內
        decimal peak = startEquity;
        decimal maxDD = 0m;
        foreach (var p in curveInTest)
        {
            if (p.Value > peak) peak = p.Value;
            if (peak > 0 && (peak - p.Value) / peak > maxDD)
                maxDD = (peak - p.Value) / peak;
        }
        m.MaxDrawdownPct = Math.Round(maxDD * 100m, 4);

        // 3. Sharpe：test 區間內的日報酬率
        var combined = new List<BacktestEngine.EquityPoint>();
        if (curveBeforeTest.Count > 0) combined.Add(curveBeforeTest.Last());
        combined.AddRange(curveInTest);
        var returns = new List<decimal>();
        for (int i = 1; i < combined.Count; i++)
        {
            var prev = combined[i - 1].Value;
            if (prev > 0) returns.Add((combined[i].Value - prev) / prev);
        }
        if (returns.Count >= 2)
        {
            var mean = returns.Average();
            var variance = returns.Sum(r => (r - mean) * (r - mean)) / (returns.Count - 1);
            var stdDev = (decimal)Math.Sqrt((double)variance);
            if (stdDev > 0)
                m.Sharpe = Math.Round(mean / stdDev * (decimal)Math.Sqrt(252.0), 4);
        }

        // 4. Trades 與 WinRate：EntryDate 在 test 區間內的
        var oosTrades = full.Trades.Where(t => t.EntryDate >= testStart).ToList();
        m.Trades = oosTrades.Count;
        if (oosTrades.Count > 0)
            m.WinRate = Math.Round((decimal)oosTrades.Count(t => t.Pnl > 0) / oosTrades.Count, 4);

        return m;
    }

    // ── 套用最佳參數到 config ─────────────────────────────────────────

    private static StrategyConfig ApplySmaParams(StrategyConfig baseConfig, Dictionary<string, int> p)
    {
        return new StrategyConfig
        {
            Symbol = baseConfig.Symbol,
            Exchange = baseConfig.Exchange,
            Name = "sma_cross",
            SmaFast = p.TryGetValue("sma_fast", out var f) ? f : baseConfig.SmaFast,
            SmaSlow = p.TryGetValue("sma_slow", out var s) ? s : baseConfig.SmaSlow,
            RsiPeriod = baseConfig.RsiPeriod,
            MacdFast = baseConfig.MacdFast,
            MacdSlow = baseConfig.MacdSlow,
            MacdSignal = baseConfig.MacdSignal,
        };
    }

    private static StrategyConfig ApplyRsiParams(StrategyConfig baseConfig, Dictionary<string, int> p)
    {
        return new StrategyConfig
        {
            Symbol = baseConfig.Symbol,
            Exchange = baseConfig.Exchange,
            Name = "rsi_oversold",
            RsiPeriod = p.TryGetValue("rsi_period", out var pd) ? pd : baseConfig.RsiPeriod,
            RsiOversold = p.TryGetValue("rsi_oversold", out var o) ? o : (int)baseConfig.RsiOversold,
            RsiOverbought = p.TryGetValue("rsi_overbought", out var ob) ? ob : (int)baseConfig.RsiOverbought,
            SmaFast = baseConfig.SmaFast,
            SmaSlow = baseConfig.SmaSlow,
        };
    }
}
