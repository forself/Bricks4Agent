using FluentAssertions;
using StrategyWorker.Engine;
using StrategyWorker.Models;
using Xunit;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// RegimeAttributionAnalyzer:量化「分行情用對策略」vs「單一策略到底」改善多少。
/// 關鍵不變量:Oracle(每 regime 事後挑最佳之和)在數學上必 ≥ 任何單策略全程 → 改善% ≥ 0。
/// </summary>
public class RegimeAttributionTests
{
    private static StrategyConfig Cfg() => new() { Symbol = "X", Exchange = "test", Interval = "4h" };

    private static IReadOnlyList<IStrategy> Strats() => new IStrategy[]
    {
        new SmaCrossStrategy(),   // 趨勢
        new RsiStrategy(),        // 均值回歸
        new SuperTrendStrategy(), // 趨勢跟隨
    };

    /// <summary>前半上升趨勢、後半震盪 → 讓 RegimeDetector 標出不同 regime。</summary>
    private static List<BarData> MixedRegimeBars(int n)
    {
        var bars = new List<BarData>();
        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        decimal price = 100m;
        for (int i = 0; i < n; i++)
        {
            decimal open = price;
            decimal close = i < n / 2
                ? 100m + i * 0.5m                              // 趨勢段
                : 150m + (decimal)Math.Sin(i / 4.0) * 5m;     // 震盪段
            bars.Add(new BarData
            {
                OpenTime = t0.AddHours(i * 4), Open = open,
                High = Math.Max(open, close) + 1m, Low = Math.Min(open, close) - 1m,
                Close = close, Volume = 1_000_000m,
            });
            price = close;
        }
        return bars;
    }

    [Fact]
    public void Analyze_TooFewBars_ReturnsError()
        => RegimeAttributionAnalyzer.Analyze(MixedRegimeBars(40), Strats(), Cfg())
            .Error.Should().NotBeNull();

    [Fact]
    public void Analyze_OracleAtLeastBestSingle()
    {
        var r = RegimeAttributionAnalyzer.Analyze(MixedRegimeBars(220), Strats(), Cfg());
        r.Error.Should().BeNull();
        // 數學不變量:每 regime 取最佳之和 ≥ 任何固定單策略的全程和
        r.OracleReturnPct.Should().BeGreaterThanOrEqualTo(r.BestSingleReturnPct);
        r.ImprovementPctVsBestSingle.Should().BeGreaterThanOrEqualTo(0m);
    }

    [Fact]
    public void Analyze_PopulatesMatrixAndRegimes()
    {
        var r = RegimeAttributionAnalyzer.Analyze(MixedRegimeBars(220), Strats(), Cfg());
        r.RegimeBars.Should().NotBeEmpty();
        r.Matrix.Should().ContainKey("sma_cross");
        r.PerRegimeBest.Should().NotBeEmpty();
        r.BestSingleStrategy.Should().NotBeNullOrEmpty();
        r.AnalyzedBars.Should().BeGreaterThan(0);
    }
}
