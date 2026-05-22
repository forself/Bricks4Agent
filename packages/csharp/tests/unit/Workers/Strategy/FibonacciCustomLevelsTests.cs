using FluentAssertions;
using StrategyWorker.Engine;
using StrategyWorker.Engine.Indicators;
using Xunit;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// 用戶(AnthonyLee)修正版斐波那契點位的測試:
/// 自訂比率(0.707 / 0.886 / 1.13 / 1.33 / 2.24)有進到比率組,且算出的點位對得上他 BTC 圖上的實際價;
/// FibonacciStrategy 進場區做成可優化參數。
/// </summary>
public class FibonacciCustomLevelsTests
{
    [Fact]
    public void RetracementAndExtensionRatios_IncludeUserCustomLevels()
    {
        FibonacciLevels.RetracementRatios.Should().Contain(new[] { 0.707m, 0.886m });
        FibonacciLevels.ExtensionRatios.Should().Contain(new[] { 1.13m, 1.33m, 2.24m });
    }

    [Fact]
    public void Levels_MatchUserBtcChart()
    {
        // 用戶 BTC 圖:0 = 53,101.19、1 = 76,061.75;low + r*range 對應圖上的價(Levels 的 "down" 分支)。
        var lv = FibonacciLevels.Levels(76061.75m, 53101.19m, "down");
        lv[0.618m].Should().BeApproximately(67290.81m, 1m);
        lv[0.707m].Should().BeApproximately(69334.30m, 1m);
        lv[0.886m].Should().BeApproximately(73444.25m, 1m);
    }

    [Fact]
    public void FibonacciStrategy_IsOptimizable()
    {
        new FibonacciStrategy().ParamSchema.Keys
            .Should().Contain(new[] { "fib_zone_low", "fib_zone_high" });
    }

    [Fact]
    public void ExitExtension_AdaptsToRegime()
    {
        // 用戶設計:牛市出場常衝 2.24、熊市常停 1.33、其他取 1.618
        FibonacciStrategy.ExitExtensionForRegime(RegimeDetector.RegimeType.TrendingUp).Should().Be(2.24m);
        FibonacciStrategy.ExitExtensionForRegime(RegimeDetector.RegimeType.TrendingDown).Should().Be(1.33m);
        FibonacciStrategy.ExitExtensionForRegime(RegimeDetector.RegimeType.RangeBound).Should().Be(1.618m);
    }
}
