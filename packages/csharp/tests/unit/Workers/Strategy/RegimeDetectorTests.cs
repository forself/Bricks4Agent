using StrategyWorker.Engine;
using StrategyWorker.Engine.Indicators;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// 鎖住 regime 分類門檻——後續調整這些常數時，這些 case 要明確失敗、提醒人去看
/// AutoSelectStrategy 的對照表是否還合理。
/// </summary>
public class RegimeDetectorTests
{
    private static List<BarData> Make(int count, Func<int, decimal> closeAt, decimal atrPct = 0m)
    {
        // atrPct = 0 表示 high/low 跟 close 同價、ATR=0；> 0 時用 close × atrPct% 撐開上下範圍
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, count).Select(i =>
        {
            var c = closeAt(i);
            var range = atrPct > 0m ? c * atrPct / 100m : 0m;
            return new BarData
            {
                OpenTime = t0.AddDays(i),
                Open = c, High = c + range, Low = c - range, Close = c, Volume = 1000m,
            };
        }).ToList();
    }

    [Fact]
    public void TooFewBars_ReturnsUnclear()
    {
        var bars = Make(30, _ => 100m);

        var r = RegimeDetector.Detect(bars);

        r.Type.Should().Be(RegimeDetector.RegimeType.Unclear);
        r.Description.Should().Contain("not enough");
    }

    [Fact]
    public void StrongUptrend_ReturnsTrendingUp()
    {
        // 70 根線性上漲、SMA50 斜率 ≥ 1%、價格在 SMA50 上方
        var bars = Make(70, i => 100m + i * 0.5m);

        var r = RegimeDetector.Detect(bars);

        r.Type.Should().Be(RegimeDetector.RegimeType.TrendingUp);
        r.Sma50Slope.Should().BeGreaterThan(1.0m);
        r.AboveSma50.Should().BeTrue();
    }

    [Fact]
    public void StrongDowntrend_ReturnsTrendingDown()
    {
        // 70 根線性下跌：start 200、每根 -0.5
        var bars = Make(70, i => 200m - i * 0.5m);

        var r = RegimeDetector.Detect(bars);

        r.Type.Should().Be(RegimeDetector.RegimeType.TrendingDown);
        r.Sma50Slope.Should().BeLessThan(-1.0m);
        r.AboveSma50.Should().BeFalse();
    }

    [Fact]
    public void FlatPrice_ReturnsRangeBoundOrSqueeze()
    {
        // 完全平盤——BB width = 0（所有收盤同價、stddev 0）→ < 3% 視為 squeeze
        var bars = Make(70, _ => 100m);

        var r = RegimeDetector.Detect(bars);

        // BB width = 0 < 3%，但函式只在 bbWidth > 0 才走 Squeeze 分支、所以平盤會落在 RangeBound
        r.Type.Should().Be(RegimeDetector.RegimeType.RangeBound);
        Math.Abs(r.Sma50Slope).Should().BeLessThan(0.3m);
    }

    [Fact]
    public void HighVolatility_OverridesTrendDetection()
    {
        // 即使有上升趨勢、ATR% > 4% 也會被歸成 HighVol（避免趨勢策略在大波動時亂跑）
        var bars = Make(70, i => 100m + i * 0.5m, atrPct: 5m);

        var r = RegimeDetector.Detect(bars);

        r.Type.Should().Be(RegimeDetector.RegimeType.HighVol);
        r.AtrPct.Should().BeGreaterThan(4m);
    }

    [Fact]
    public void GentleUptrend_BB_NarrowEnough_ReturnsSqueeze()
    {
        // 線性微漲（每根 +0.05）：價格 stddev 很小、BB width < 3% → Squeeze
        // 即使方向略偏向上、波動極小時策略應該等爆破而非追趨勢
        var bars = Make(70, i => 100m + i * 0.05m);

        var r = RegimeDetector.Detect(bars);

        r.Type.Should().Be(RegimeDetector.RegimeType.Squeeze);
        r.BbWidth.Should().BeLessThan(3m);
    }
}
