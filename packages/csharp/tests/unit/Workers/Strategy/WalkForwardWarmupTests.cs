using StrategyWorker.Engine;
using StrategyWorker.Models;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// 鎖死 walk-forward warmup 修正:OOS test 窗要餵 [train+test] 整段、只從 test 起點交易
/// (tradeStartIndex),否則「MinBars > testBars 的策略」在 bare test slice 永遠 hold → 0 OOS 交易
/// (volatility_breakout 的 0/0 就是這個 bug)。
/// </summary>
public class WalkForwardWarmupTests
{
    /// <summary>需要 ≥100 根才出手的策略(模擬 volatility_breakout 這種高 MinBars 指標)。</summary>
    private sealed class HighMinBarsBuyStub : IStrategy
    {
        public string Name => "high_minbars";
        public int MinBars => 100;
        public Signal Evaluate(List<BarData> bars, StrategyConfig config) => new()
        {
            SignalId = "stub", Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
            Action = bars.Count >= 100 ? "buy" : "hold",
            Confidence = bars.Count >= 100 ? 0.7m : 0m,
            Reason = "high minbars", Interval = config.Interval,
        };
    }

    private static List<BarData> LinearUp(int n)
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, n).Select(i =>
        {
            var c = 100m + i * 0.5m;
            return new BarData { OpenTime = t0.AddDays(i), Open = c, High = c, Low = c, Close = c, Volume = 1000m };
        }).ToList();
    }

    [Fact]
    public void HighMinBarsStrategy_TradesInOos_AfterWarmupFix()
    {
        // train=365 / test=90：bare 90 根 test slice 下 MinBars=100 的策略永遠 hold(舊 bug)。
        // 修正後 test 窗有 train 當 warmup → 在 OOS 真的進場 → 線性上漲下 OOS 報酬為正。
        var bars = LinearUp(600);
        var cfg = new StrategyConfig { Symbol = "X", Exchange = "bingx", Interval = "1d" };

        var wf = BacktestEngine.RunWalkForward(new HighMinBarsBuyStub(), bars, cfg,
            trainBars: 365, testBars: 90, stride: 90);

        wf.TotalFolds.Should().BeGreaterThan(0);
        wf.AvgTestReturnPct.Should().BeGreaterThan(0m);   // 有在 OOS 交易(修好前會是 0)
    }
}
