using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 策略參數優化器 — 暴力搜索最佳參數組合。
/// 用回測引擎跑多組參數，找出 Sharpe Ratio 最高的。
/// </summary>
public static class ParameterOptimizer
{
    public class OptimizeResult
    {
        public string Strategy { get; set; } = "";
        public string Symbol { get; set; } = "";
        public int TotalCombinations { get; set; }
        public decimal BestSharpe { get; set; }
        public decimal BestReturn { get; set; }
        public decimal BestWinRate { get; set; }
        public Dictionary<string, int> BestParams { get; set; } = new();
        public List<ParamResult> TopResults { get; set; } = new();
    }

    public class ParamResult
    {
        public Dictionary<string, int> Params { get; set; } = new();
        public decimal TotalReturnPct { get; set; }
        public decimal Sharpe { get; set; }
        public decimal WinRate { get; set; }
        public decimal MaxDrawdownPct { get; set; }
        public int Trades { get; set; }
    }

    /// <summary>
    /// 優化 SMA Cross 策略的 fast/slow 參數。
    /// </summary>
    public static OptimizeResult OptimizeSma(List<BarData> bars, StrategyConfig baseConfig, decimal cash = 100_000)
    {
        var results = new List<ParamResult>();
        var fastRange = new[] { 5, 8, 10, 12, 15, 20 };
        var slowRange = new[] { 20, 25, 30, 40, 50, 60 };

        foreach (var fast in fastRange)
        foreach (var slow in slowRange)
        {
            if (fast >= slow) continue;
            var config = new StrategyConfig
            {
                Symbol = baseConfig.Symbol, Exchange = baseConfig.Exchange,
                SmaFast = fast, SmaSlow = slow,
                RsiPeriod = baseConfig.RsiPeriod, MacdFast = baseConfig.MacdFast,
                MacdSlow = baseConfig.MacdSlow, MacdSignal = baseConfig.MacdSignal,
            };
            var bt = BacktestEngine.Run(new SmaCrossStrategy(), bars, config, cash);
            results.Add(new ParamResult
            {
                Params = new() { ["sma_fast"] = fast, ["sma_slow"] = slow },
                TotalReturnPct = bt.TotalReturnPct, Sharpe = bt.SharpeRatio,
                WinRate = bt.WinRate, MaxDrawdownPct = bt.MaxDrawdownPct, Trades = bt.TotalTrades,
            });
        }

        return BuildResult("sma_cross", baseConfig.Symbol, results);
    }

    /// <summary>
    /// 優化 RSI 策略的 period / oversold / overbought。
    /// </summary>
    public static OptimizeResult OptimizeRsi(List<BarData> bars, StrategyConfig baseConfig, decimal cash = 100_000)
    {
        var results = new List<ParamResult>();
        var periods = new[] { 7, 10, 14, 20, 28 };
        var osLevels = new[] { 20, 25, 30, 35 };
        var obLevels = new[] { 65, 70, 75, 80 };

        foreach (var period in periods)
        foreach (var os in osLevels)
        foreach (var ob in obLevels)
        {
            if (os >= ob) continue;
            var config = new StrategyConfig
            {
                Symbol = baseConfig.Symbol, Exchange = baseConfig.Exchange,
                RsiPeriod = period, RsiOversold = os, RsiOverbought = ob,
                SmaFast = baseConfig.SmaFast, SmaSlow = baseConfig.SmaSlow,
            };
            var bt = BacktestEngine.Run(new RsiStrategy(), bars, config, cash);
            results.Add(new ParamResult
            {
                Params = new() { ["rsi_period"] = period, ["rsi_oversold"] = os, ["rsi_overbought"] = ob },
                TotalReturnPct = bt.TotalReturnPct, Sharpe = bt.SharpeRatio,
                WinRate = bt.WinRate, MaxDrawdownPct = bt.MaxDrawdownPct, Trades = bt.TotalTrades,
            });
        }

        return BuildResult("rsi_oversold", baseConfig.Symbol, results);
    }

    private static OptimizeResult BuildResult(string strategy, string symbol, List<ParamResult> results)
    {
        var sorted = results.Where(r => r.Trades > 0).OrderByDescending(r => r.Sharpe).ToList();
        var best = sorted.FirstOrDefault();

        return new OptimizeResult
        {
            Strategy = strategy, Symbol = symbol,
            TotalCombinations = results.Count,
            BestSharpe = best?.Sharpe ?? 0,
            BestReturn = best?.TotalReturnPct ?? 0,
            BestWinRate = best?.WinRate ?? 0,
            BestParams = best?.Params ?? new(),
            TopResults = sorted.Take(10).ToList(),
        };
    }
}
