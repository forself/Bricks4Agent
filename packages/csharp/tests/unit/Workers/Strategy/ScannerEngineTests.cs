using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using StrategyWorker.Engine;
using StrategyWorker.Models;
using Xunit;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// ScannerEngine(universe 掃描)契約測試。
/// 掃描分數本身由既有指標(harmonic / price action / SMC)決定,這裡只驗「掃描引擎的聚合契約」:
/// 依 magnitude 由大到小排序、尊重 Top N、過濾 minMagnitude、bar 不足回 null。
/// </summary>
public class ScannerEngineTests
{
    private static BarData B(int i, decimal o, decimal h, decimal l, decimal c) => new()
    {
        OpenTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i * 4),
        Open = o, High = h, Low = l, Close = c, Volume = 1_000_000,
    };

    /// <summary>產一段帶趨勢 + 波動的 K 線(不同參數讓各 symbol 形狀不同)。</summary>
    private static List<BarData> Series(int count, decimal start, decimal driftPerBar, decimal wiggle)
    {
        var bars = new List<BarData>();
        decimal price = start;
        for (int i = 0; i < count; i++)
        {
            decimal open = price;
            decimal osc = (i % 2 == 0) ? wiggle : -wiggle;
            decimal close = open + driftPerBar + osc;
            decimal high = Math.Max(open, close) + wiggle;
            decimal low = Math.Min(open, close) - wiggle;
            bars.Add(B(i, open, high, low, close));
            price = close;
        }
        return bars;
    }

    [Fact]
    public void ScanSymbol_TooFewBars_ReturnsNull()
    {
        var bars = Series(ScannerEngine.MinBars - 1, 100m, 0.2m, 0.5m);
        ScannerEngine.ScanSymbol("X", bars).Should().BeNull();
    }

    [Fact]
    public void ScanUniverse_SortedByMagnitudeDesc_AndWithinTopN()
    {
        var universe = new Dictionary<string, List<BarData>>
        {
            ["UP"]    = Series(80, 100m,  0.8m, 1.0m),
            ["DOWN"]  = Series(80, 200m, -0.8m, 1.0m),
            ["FLAT"]  = Series(80, 100m,  0.0m, 0.3m),
            ["CHOP"]  = Series(80, 150m,  0.0m, 2.5m),
            ["RISE2"] = Series(80,  50m,  0.5m, 0.8m),
        };

        var top = ScannerEngine.ScanUniverse(universe, minMagnitude: 0m, topN: 3);

        top.Count.Should().BeLessThanOrEqualTo(3);
        top.Select(r => r.Magnitude).Should().BeInDescendingOrder();
        top.Should().OnlyContain(r => r.Magnitude >= 0m);
    }

    [Fact]
    public void ScanUniverse_HighMinMagnitude_FiltersAllOut()
    {
        var universe = new Dictionary<string, List<BarData>>
        {
            ["A"] = Series(80, 100m,  0.3m, 0.5m),
            ["B"] = Series(80, 100m, -0.3m, 0.5m),
        };

        ScannerEngine.ScanUniverse(universe, minMagnitude: 9999m, topN: 10)
            .Should().BeEmpty();
    }
}
