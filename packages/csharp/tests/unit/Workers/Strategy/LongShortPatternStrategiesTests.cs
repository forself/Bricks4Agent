using FluentAssertions;
using StrategyWorker.Engine;
using StrategyWorker.Models;
using Xunit;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// 2026-05-24 第三批(形態工具,多空):
///   fib_retrace_ls(斐波那契回撤,自寫)、harmonic_ls(諧波反轉,重用 HarmonicPatterns)。
/// 契約 + 決定性 + fib locality + fib 多空方向分支 + 多空引擎整合。
/// (harmonic 形態難以合成、不寫「強制觸發」測試;靠契約 + 引擎整合。)
/// </summary>
public class LongShortPatternStrategiesTests
{
    private static IStrategy[] All() => new IStrategy[]
    {
        new FibRetraceLsStrategy(),
        new HarmonicLsStrategy(),
    };

    private static StrategyConfig Cfg() => new() { Symbol = "BTC-USDT", Exchange = "binance", Interval = "1d" };

    private static List<BarData> MakeSynthetic(int n = 500, int seed = 13, double drift = 0.0004, double sigma = 0.025)
    {
        var rng = new Random(seed);
        var t0 = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<BarData>(n);
        double close = 100.0;
        for (int i = 0; i < n; i++)
        {
            double ret = drift + sigma * NextGaussian(rng);
            close *= Math.Exp(ret);
            bars.Add(new BarData
            {
                OpenTime = t0.AddDays(i),
                Open = (decimal)(close * 0.999), High = (decimal)(close * 1.01),
                Low = (decimal)(close * 0.99), Close = (decimal)close, Volume = rng.Next(1_000_000, 5_000_000),
            });
        }
        return bars;
    }

    private static double NextGaussian(Random rng)
    {
        var u1 = 1.0 - rng.NextDouble(); var u2 = 1.0 - rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    // 帶輕微影線的 K 線
    private static void Add(List<BarData> bars, DateTime t0, int i, decimal px) => bars.Add(new BarData
    {
        OpenTime = t0.AddDays(i), Open = px, High = px * 1.001m, Low = px * 0.999m, Close = px, Volume = 1_000_000m,
    });

    [Fact]
    public void AllStrategies_TooFewBars_Hold()
    {
        var tiny = MakeSynthetic(20);
        foreach (var s in All())
            s.Evaluate(tiny, Cfg()).Action.Should().Be("hold", $"{s.Name} 資料不足應 hold");
    }

    [Fact]
    public void AllStrategies_ProduceValidSignal()
    {
        var bars = MakeSynthetic(500);
        foreach (var s in All())
        {
            var sig = s.Evaluate(bars, Cfg());
            sig.Action.Should().BeOneOf("buy", "sell", "hold");
            sig.Confidence.Should().BeInRange(0m, 1m, $"{s.Name}");
            sig.Strategy.Should().Be(s.Name);
        }
    }

    [Fact]
    public void AllStrategies_AreOptimizable()
    {
        new FibRetraceLsStrategy().ParamSchema.Keys.Should().Contain(new[] { "fib_lookback", "fib_min_range_pct" });
        new HarmonicLsStrategy().ParamSchema.Keys.Should().Contain(new[] { "hm_pivot_window", "hm_entry_window", "hm_min_conf" });
    }

    [Fact]
    public void AllStrategies_Deterministic()
    {
        var bars = MakeSynthetic(400);
        foreach (var s in All())
        {
            var a = s.Evaluate(bars, Cfg()); var b = s.Evaluate(bars, Cfg());
            a.Action.Should().Be(b.Action, $"{s.Name}");
            a.Confidence.Should().Be(b.Confidence, $"{s.Name}");
        }
    }

    [Theory]
    [InlineData(60)]
    [InlineData(120)]
    public void Fib_DropOldHistory_IsInvariant(int dropFront)
    {
        // fib_retrace_ls 只用最後 lookback 根 → 砍更舊歷史不影響末端訊號(純回看)。
        var full = MakeSynthetic(500, 9);
        var trimmed = full.Skip(dropFront).ToList();   // 保留 ≥380 根 ≫ lookback(60)
        var s = new FibRetraceLsStrategy();
        var a = s.Evaluate(full, Cfg());
        var b = s.Evaluate(trimmed, Cfg());
        b.Action.Should().Be(a.Action, "fib 砍舊歷史後訊號變了 → 非純回看");
        b.Confidence.Should().Be(a.Confidence);
    }

    [Fact]
    public void Fib_UptrendPullbackToGoldenZone_Buys()
    {
        // 視窗內:低在前(idx20=100)、高在中(idx55=200)、當前回撤到 ~0.6 黃金區(160)→ 升勢做多。
        var bars = new List<BarData>();
        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 20; i++) Add(bars, t0, i, 100m);
        for (int i = 20; i <= 55; i++) Add(bars, t0, i, 100m + (200m - 100m) * (i - 20) / 35m);   // 升 100→200
        for (int i = 56; i < 80; i++) Add(bars, t0, i, 200m - (200m - 160m) * (i - 55) / 24m);     // 回 200→160

        var sig = new FibRetraceLsStrategy().Evaluate(bars, Cfg());
        sig.Action.Should().Be("buy", "升勢回撤黃金區應做多");
        sig.Confidence.Should().BeGreaterThanOrEqualTo(0.6m);
        sig.Indicators.Should().ContainKey("ext_1618");   // Fib 擴展目標有用上
    }

    [Fact]
    public void Fib_DowntrendRallyToGoldenZone_Shorts()
    {
        // 視窗內:高在前、低在中、當前反彈到 ~0.4 黃金區 → 跌勢做空。
        var bars = new List<BarData>();
        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 20; i++) Add(bars, t0, i, 200m);
        for (int i = 20; i <= 55; i++) Add(bars, t0, i, 200m - (200m - 100m) * (i - 20) / 35m);   // 跌 200→100
        for (int i = 56; i < 80; i++) Add(bars, t0, i, 100m + (140m - 100m) * (i - 55) / 24m);     // 彈 100→140

        var sig = new FibRetraceLsStrategy().Evaluate(bars, Cfg());
        sig.Action.Should().Be("sell", "跌勢反彈黃金區應做空");
        sig.Confidence.Should().BeGreaterThanOrEqualTo(0.6m);
    }

    [Fact]
    public void LongShort_WalkForward_ProducesFolds()
    {
        var bars = MakeSynthetic(850);
        foreach (var s in All())
            LongShortBacktestEngine.RunWalkForward(s, bars, Cfg(), 250, 90, 60)
                .Folds.Count.Should().BeGreaterThan(0, $"{s.Name} 多空 OOS 應切出 folds");
    }
}
