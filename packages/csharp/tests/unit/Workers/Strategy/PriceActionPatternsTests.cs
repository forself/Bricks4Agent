using StrategyWorker.Engine;
using StrategyWorker.Engine.Indicators;
using PaDirection = StrategyWorker.Engine.Indicators.PriceActionPatterns.Direction;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// PriceActionPatterns 6 種型態的契約測試。
///
/// 每組測試用手刻 OHLC 構造一個目標型態出現的場景、確認偵測器抓到。
/// 同時驗 no-lookahead invariant（任意截斷後同樣的型態應仍偵測得到）。
/// </summary>
public class PriceActionPatternsTests
{
    private static BarData Bar(decimal o, decimal h, decimal l, decimal c, int day = 1)
        => new()
        {
            // 用 AddDays 處理 day > 31 的情況（DetectAll 過濾測試會塞 40+ 根）
            OpenTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(day - 1),
            Open  = o, High = h, Low = l, Close = c, Volume = 1000m,
        };

    // ── Engulfing ──────────────────────────────────────────────

    [Fact]
    public void Engulfing_Bullish_Detected()
    {
        // 前一根：跌 (open=105, close=100)、實體 5
        // 當前根：漲 (open=99, close=110)、實體 11 → 完全吞掉
        var bars = new List<BarData>
        {
            Bar(105m, 106m,  99m, 100m, day: 1),
            Bar( 99m, 111m,  98m, 110m, day: 2),
        };
        var det = PriceActionPatterns.DetectEngulfing(bars);
        det.Should().ContainSingle(d => d.Type == "Bullish_Engulfing");
        det[0].Direction.Should().Be(PaDirection.Bullish);
        det[0].BarIndex.Should().Be(1);
    }

    [Fact]
    public void Engulfing_Bearish_Detected()
    {
        var bars = new List<BarData>
        {
            Bar(100m, 106m,  99m, 105m, day: 1),    // 漲、實體 5
            Bar(106m, 107m,  94m,  95m, day: 2),    // 跌、實體 11、吞噬
        };
        var det = PriceActionPatterns.DetectEngulfing(bars);
        det.Should().ContainSingle(d => d.Type == "Bearish_Engulfing");
        det[0].Direction.Should().Be(PaDirection.Bearish);
    }

    [Fact]
    public void Engulfing_NotEngulf_NoDetection()
    {
        // 當前實體沒包住前一根 → 不偵測
        var bars = new List<BarData>
        {
            Bar(105m, 110m,  99m, 100m, day: 1),
            Bar(101m, 108m, 100m, 103m, day: 2),
        };
        PriceActionPatterns.DetectEngulfing(bars).Should().BeEmpty();
    }

    // ── Pin Bar ────────────────────────────────────────────────

    [Fact]
    public void PinBar_Hammer_Detected()
    {
        // 開 100 收 101（多、實體 1）、最低 95（下影 5）、上影 0.5
        var bars = new List<BarData> { Bar(100m, 101.5m, 95m, 101m) };
        var det = PriceActionPatterns.DetectPinBar(bars);
        det.Should().ContainSingle(d => d.Type == "Hammer");
        det[0].Direction.Should().Be(PaDirection.Bullish);
    }

    [Fact]
    public void PinBar_ShootingStar_Detected()
    {
        // 開 101 收 100（空、實體 1）、最高 106（上影 5）、下影 0.5
        var bars = new List<BarData> { Bar(101m, 106m, 99.5m, 100m) };
        var det = PriceActionPatterns.DetectPinBar(bars);
        det.Should().ContainSingle(d => d.Type == "Shooting_Star");
        det[0].Direction.Should().Be(PaDirection.Bearish);
    }

    [Fact]
    public void PinBar_NormalCandle_NoDetection()
    {
        // 平均型 K 棒、影線跟實體差不多 → 不偵測
        var bars = new List<BarData> { Bar(100m, 102m, 99m, 101m) };
        PriceActionPatterns.DetectPinBar(bars).Should().BeEmpty();
    }

    // ── Inside Bar ─────────────────────────────────────────────

    [Fact]
    public void InsideBar_Detected()
    {
        var bars = new List<BarData>
        {
            Bar(100m, 110m, 90m, 105m, day: 1),     // 範圍 [90, 110]
            Bar(101m, 108m, 95m, 103m, day: 2),     // 範圍 [95, 108] ⊂ [90, 110]
        };
        var det = PriceActionPatterns.DetectInsideBar(bars);
        det.Should().ContainSingle(d => d.Type == "Inside_Bar");
        det[0].Direction.Should().Be(PaDirection.Neutral);
    }

    // ── Outside Bar ────────────────────────────────────────────

    [Fact]
    public void OutsideBar_Bullish_Detected()
    {
        var bars = new List<BarData>
        {
            Bar(100m, 105m, 98m, 102m, day: 1),     // 範圍 [98, 105]
            Bar(101m, 110m, 95m, 108m, day: 2),     // 範圍 [95, 110] ⊃；close>open=多
        };
        var det = PriceActionPatterns.DetectOutsideBar(bars);
        det.Should().ContainSingle(d => d.Type == "Bullish_Outside_Bar");
        det[0].Direction.Should().Be(PaDirection.Bullish);
    }

    // ── Star ───────────────────────────────────────────────────

    [Fact]
    public void Star_Morning_Detected()
    {
        var bars = new List<BarData>
        {
            Bar(110m, 110m, 99m, 100m, day: 1),     // bar1 跌、實體 10
            Bar(100m, 101m, 99m, 100.5m, day: 2),   // bar2 小體 0.5（< 10*0.3=3）
            Bar(100m, 112m, 99m, 110m, day: 3),     // bar3 漲、實體 10、收 110 > mid1=105
        };
        var det = PriceActionPatterns.DetectStar(bars);
        det.Should().ContainSingle(d => d.Type == "Morning_Star");
        det[0].Direction.Should().Be(PaDirection.Bullish);
    }

    [Fact]
    public void Star_Evening_Detected()
    {
        var bars = new List<BarData>
        {
            Bar(100m, 110m, 99m, 110m, day: 1),
            Bar(110m, 111m, 109m, 109.5m, day: 2), // 小體
            Bar(110m, 111m, 98m, 100m, day: 3),    // 大跌
        };
        var det = PriceActionPatterns.DetectStar(bars);
        det.Should().ContainSingle(d => d.Type == "Evening_Star");
        det[0].Direction.Should().Be(PaDirection.Bearish);
    }

    // ── Doji ───────────────────────────────────────────────────

    [Fact]
    public void Doji_Detected()
    {
        // 實體很小、range 大、body/range < 10%
        var bars = new List<BarData> { Bar(100m, 105m, 95m, 100.5m) };
        var det = PriceActionPatterns.DetectDoji(bars);
        det.Should().ContainSingle(d => d.Type == "Doji");
        det[0].Direction.Should().Be(PaDirection.Neutral);
    }

    [Fact]
    public void Doji_LargeBody_NoDetection()
    {
        // 實體大、不是 doji
        var bars = new List<BarData> { Bar(100m, 105m, 99m, 104m) };
        PriceActionPatterns.DetectDoji(bars).Should().BeEmpty();
    }

    // ── DetectAll：彙整 + maxAgeBars filter ─────────────────────

    [Fact]
    public void DetectAll_FiltersOldDetectionsByMaxAgeBars()
    {
        var bars = new List<BarData>();
        // bar 0: bullish engulfing setup 前面
        bars.Add(Bar(105m, 106m, 99m, 100m, day: 1));
        bars.Add(Bar(99m, 111m, 98m, 110m, day: 2));      // 多吞、bar 1
        // 後面塞 40 根普通 K 線
        for (int i = 0; i < 40; i++)
        {
            bars.Add(Bar(100m, 101m, 99m, 100m, day: 3 + i));
        }
        var allWindow30 = PriceActionPatterns.DetectAll(bars, maxAgeBars: 30);
        // bar=1 距離當前 (bars.Count-1 = 41) 是 40 根、cutoff = 41-30=11
        // bar 1 < 11、應被過濾掉
        allWindow30.Should().NotContain(d => d.BarIndex == 1);

        var allWindowLarge = PriceActionPatterns.DetectAll(bars, maxAgeBars: 50);
        allWindowLarge.Should().Contain(d => d.BarIndex == 1 && d.Type == "Bullish_Engulfing");
    }

    // ── No-Lookahead invariant ──────────────────────────────────

    [Fact]
    public void DetectAll_NoLookahead_TruncationStable()
    {
        // 構造一段在第 5 根出現的 bullish engulfing
        var full = new List<BarData>();
        for (int i = 0; i < 30; i++) full.Add(Bar(100m, 101m, 99m, 100m, day: i + 1));
        // 改第 4/5 根成 bull engulfing
        full[4] = Bar(105m, 106m, 99m, 100m, day: 5);     // 跌
        full[5] = Bar(99m, 111m, 98m, 110m, day: 6);      // 多大吞噬

        var detFull = PriceActionPatterns.DetectAll(full, maxAgeBars: 100);
        var detTruncated = PriceActionPatterns.DetectAll(full.Take(10).ToList(), maxAgeBars: 100);

        // bar 5 的 bullish engulfing 在兩邊應都出現、型別/方向應一致
        detFull.Should().Contain(d => d.BarIndex == 5 && d.Type == "Bullish_Engulfing");
        detTruncated.Should().Contain(d => d.BarIndex == 5 && d.Type == "Bullish_Engulfing");
    }
}
