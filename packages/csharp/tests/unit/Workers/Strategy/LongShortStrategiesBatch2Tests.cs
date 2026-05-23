using FluentAssertions;
using StrategyWorker.Engine;
using StrategyWorker.Models;
using Xunit;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// 2026-05-24 第二批「原生多空」策略測試:
///   dual_mom_ls / di_trend_ls / supertrend_ls / bb_revert_ls / donchian_fade_ls
///
/// 涵蓋:契約(資料不足 hold、輸出合法、多/空信心≥0.6、可掃參、決定性)、
/// locality 不變式(視窗化策略砍最舊歷史後末端訊號不變;SuperTrend 路徑相依、另由 repo SuperTrend
/// truncation 測試覆蓋故排除)、多空引擎整合(反手做空、OOS folds)。純合成資料、固定 seed。
/// </summary>
public class LongShortStrategiesBatch2Tests
{
    private static IStrategy[] All() => new IStrategy[]
    {
        new DualMomentumLsStrategy(),
        new DiTrendLsStrategy(),
        new SuperTrendLsStrategy(),
        new BollingerRevertLsStrategy(),
        new DonchianFadeLsStrategy(),
    };

    // 砍前段不變的(視窗化)子集——排除路徑相依的 SuperTrend
    private static IStrategy[] WindowedOnly() => new IStrategy[]
    {
        new DualMomentumLsStrategy(),
        new DiTrendLsStrategy(),
        new BollingerRevertLsStrategy(),
        new DonchianFadeLsStrategy(),
    };

    private static StrategyConfig Cfg() => new() { Symbol = "BTC-USDT", Exchange = "binance", Interval = "1d" };

    private static List<BarData> MakeSynthetic(int n = 500, int seed = 11, double drift = 0.0004, double sigma = 0.025)
    {
        var rng = new Random(seed);
        var t0 = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<BarData>(n);
        double close = 100.0;
        for (int i = 0; i < n; i++)
        {
            double ret = drift + sigma * NextGaussian(rng);
            close *= Math.Exp(ret);
            double hi = Math.Abs(NextGaussian(rng)) * 0.01;
            double lo = Math.Abs(NextGaussian(rng)) * 0.01;
            double op = NextGaussian(rng) * 0.006;
            bars.Add(new BarData
            {
                OpenTime = t0.AddDays(i),
                Open = (decimal)(close * (1 + op)), High = (decimal)(close * (1 + hi)),
                Low = (decimal)(close * (1 - lo)), Close = (decimal)close, Volume = rng.Next(1_000_000, 5_000_000),
            });
        }
        return bars;
    }

    private static double NextGaussian(Random rng)
    {
        var u1 = 1.0 - rng.NextDouble(); var u2 = 1.0 - rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

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
    public void AllStrategies_LongAndShortConfidence_MeetThreshold_AndBothAppear()
    {
        bool sawBuy = false, sawSell = false;
        for (int seed = 1; seed <= 40; seed++)
        {
            // 多空都要出現:用正 drift 與負 drift 兩種市場
            foreach (var drift in new[] { 0.0015, -0.0015 })
            {
                var bars = MakeSynthetic(500, seed, drift, 0.025);
                foreach (var s in All())
                {
                    var sig = s.Evaluate(bars, Cfg());
                    if (sig.Action == "buy") { sawBuy = true; sig.Confidence.Should().BeGreaterThanOrEqualTo(0.6m, $"{s.Name} buy 信心須達門檻"); }
                    if (sig.Action == "sell") { sawSell = true; sig.Confidence.Should().BeGreaterThanOrEqualTo(0.6m, $"{s.Name} sell(做空)信心須達門檻"); }
                }
            }
        }
        sawBuy.Should().BeTrue("應出現做多訊號");
        sawSell.Should().BeTrue("應出現做空訊號(多空策略)");
    }

    [Fact]
    public void AllStrategies_AreOptimizable()
    {
        new DualMomentumLsStrategy().ParamSchema.Keys.Should().Contain(new[] { "dm_short", "dm_long" });
        new DiTrendLsStrategy().ParamSchema.Keys.Should().Contain(new[] { "di_adx_period", "di_adx_min" });
        new SuperTrendLsStrategy().ParamSchema.Keys.Should().Contain(new[] { "st_atr_period", "st_mult" });
        new BollingerRevertLsStrategy().ParamSchema.Keys.Should().Contain(new[] { "bb_period", "bb_trend_sma", "bb_entry_z" });
        new DonchianFadeLsStrategy().ParamSchema.Keys.Should().Contain(new[] { "df_lookback", "df_adx_period", "df_adx_max" });
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
    [InlineData(40)]
    [InlineData(80)]
    public void WindowedStrategies_DropOldHistory_IsInvariant(int dropFront)
    {
        var full = MakeSynthetic(500, 5);
        var trimmed = full.Skip(dropFront).ToList();   // 保留 ≥420 根、遠超最長 lookback(100)
        foreach (var s in WindowedOnly())
        {
            var a = s.Evaluate(full, Cfg());
            var b = s.Evaluate(trimmed, Cfg());
            b.Action.Should().Be(a.Action, $"{s.Name} 砍舊歷史後訊號變了 → 非純回看");
            b.Confidence.Should().Be(a.Confidence, $"{s.Name} 信心受全長影響");
        }
    }

    [Fact]
    public void LongShort_Run_ProducesBothSides()
    {
        var bars = new List<BarData>();
        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        decimal px = 100m;
        for (int i = 0; i < 150; i++) { px *= 1.01m; Add(bars, t0, i, px); }
        for (int i = 150; i < 330; i++) { px *= 0.99m; Add(bars, t0, i, px); }

        var r = LongShortBacktestEngine.Run(new DiTrendLsStrategy(), bars, Cfg());
        r.EquityCurve.Should().NotBeEmpty();
        r.Trades.Should().Contain(t => t.Side == "long");
        r.Trades.Should().Contain(t => t.Side == "short", "先漲後跌應反手做空");
    }

    [Fact]
    public void LongShort_WalkForward_ProducesFolds()
    {
        var bars = MakeSynthetic(850);
        foreach (var s in All())
            LongShortBacktestEngine.RunWalkForward(s, bars, Cfg(), 250, 90, 60)
                .Folds.Count.Should().BeGreaterThan(0, $"{s.Name} 多空 OOS 應切出 folds");
    }

    private static void Add(List<BarData> bars, DateTime t0, int i, decimal px) => bars.Add(new BarData
    {
        OpenTime = t0.AddDays(i), Open = px, High = px * 1.005m, Low = px * 0.995m, Close = px, Volume = 1_000_000m,
    });
}
