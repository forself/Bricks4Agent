using StrategyWorker.Engine;
using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// BacktestEngine 的 opt-in 停利/停損(TargetPrice / StopPrice)行為 + Fib 擴展位計算。
///   - 訊號沒給 Target/Stop → 引擎維持原行為(只靠反向訊號/收盤平倉、無盤中出場)。
///   - 給了 → 後續 K 線 high 觸 TP / low 觸 SL 即在該價位出場;同根同時觸 → SL 優先(保守)。
///   - FibonacciLevels.ExtensionLevels 投影方向正確。
/// </summary>
public class BacktestEngineTpSlTests
{
    /// <summary>第一次 Evaluate 回 buy(可帶 Target/Stop)、之後一律 hold。</summary>
    private sealed class BuyOnceStub : IStrategy
    {
        private bool _bought;
        public decimal? Tp { get; init; }
        public decimal? Sl { get; init; }
        public string Name => "buy_once";
        public Signal Evaluate(List<BarData> bars, StrategyConfig config)
        {
            if (!_bought)
            {
                _bought = true;
                return new Signal
                {
                    SignalId = "stub", Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
                    Action = "buy", Confidence = 0.7m, Reason = "buy once", Interval = config.Interval,
                    TargetPrice = Tp, StopPrice = Sl,
                };
            }
            return new Signal
            {
                SignalId = "stub", Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
                Action = "hold", Confidence = 0.5m, Reason = "hold", Interval = config.Interval,
            };
        }
    }

    private static StrategyConfig Cfg() => new() { Symbol = "BTC-USDT", Exchange = "bingx", Interval = "1d" };

    /// <summary>120 根平盤(=100)的 K 線;在 spikeIdx 那根插入指定 high/low 製造觸發。</summary>
    private static List<BarData> FlatBarsWithSpike(int spikeIdx, decimal spikeHigh, decimal spikeLow)
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, 120).Select(i =>
        {
            decimal hi = 100m, lo = 100m;
            if (i == spikeIdx) { hi = spikeHigh; lo = spikeLow; }
            return new BarData
            {
                OpenTime = t0.AddDays(i), Open = 100m, High = hi, Low = lo, Close = 100m, Volume = 1000m,
            };
        }).ToList();
    }

    [Fact]
    public void Signal_TpSl_DefaultsToNull()
    {
        var s = new Signal();
        s.TargetPrice.Should().BeNull();
        s.StopPrice.Should().BeNull();
    }

    [Fact]
    public void ExtensionLevels_Up_ProjectsAboveHigh()
    {
        var ext = FibonacciLevels.ExtensionLevels(high: 110m, low: 100m, direction: "up");
        ext[1.272m].Should().Be(112.72m);   // 100 + 1.272*10
        ext[1.618m].Should().Be(116.18m);
        ext[2.000m].Should().Be(120m);
    }

    [Fact]
    public void ExtensionLevels_Down_ProjectsBelowLow()
    {
        var ext = FibonacciLevels.ExtensionLevels(high: 110m, low: 100m, direction: "down");
        ext[1.272m].Should().Be(97.28m);    // 110 - 1.272*10
        ext[2.000m].Should().Be(90m);
    }

    [Fact]
    public void Engine_TargetPrice_ExitsAtTargetWhenHighTouches()
    {
        // 平盤 100 進場、第 110 根 high 衝到 120 → TP=110 應在 110 出場
        var bars = FlatBarsWithSpike(spikeIdx: 110, spikeHigh: 120m, spikeLow: 100m);
        var r = BacktestEngine.Run(new BuyOnceStub { Tp = 110m, Sl = 90m }, bars, Cfg(), 1000m);

        r.Trades.Should().ContainSingle();
        r.Trades[0].Side.Should().Contain("TP");
        r.Trades[0].ExitPrice.Should().Be(110m);
        r.Trades[0].Pnl.Should().BeGreaterThan(0m);
    }

    [Fact]
    public void Engine_StopPrice_ExitsAtStopWhenLowTouches()
    {
        var bars = FlatBarsWithSpike(spikeIdx: 110, spikeHigh: 100m, spikeLow: 80m);
        var r = BacktestEngine.Run(new BuyOnceStub { Tp = 110m, Sl = 90m }, bars, Cfg(), 1000m);

        r.Trades.Should().ContainSingle();
        r.Trades[0].Side.Should().Contain("SL");
        r.Trades[0].ExitPrice.Should().Be(90m);
        r.Trades[0].Pnl.Should().BeLessThan(0m);
    }

    [Fact]
    public void Engine_BothHitSameBar_StopWins()
    {
        // 同根既觸 TP(high 120) 又觸 SL(low 80) → 保守假設先觸 SL
        var bars = FlatBarsWithSpike(spikeIdx: 110, spikeHigh: 120m, spikeLow: 80m);
        var r = BacktestEngine.Run(new BuyOnceStub { Tp = 110m, Sl = 90m }, bars, Cfg(), 1000m);

        r.Trades.Should().ContainSingle();
        r.Trades[0].Side.Should().Contain("SL");
        r.Trades[0].ExitPrice.Should().Be(90m);
    }

    [Fact]
    public void Engine_NoTargetStop_NoIntrabarExit_HoldsToEnd()
    {
        // 沒給 Target/Stop → 即使 high 衝 120 也不出場、撐到最後 auto-close(維持舊行為)
        var bars = FlatBarsWithSpike(spikeIdx: 110, spikeHigh: 120m, spikeLow: 80m);
        var r = BacktestEngine.Run(new BuyOnceStub { Tp = null, Sl = null }, bars, Cfg(), 1000m);

        r.Trades.Should().ContainSingle();
        r.Trades[0].Side.Should().Contain("auto-close");
        r.Trades[0].Side.Should().NotContain("TP");
        r.Trades[0].Side.Should().NotContain("SL");
    }
}
