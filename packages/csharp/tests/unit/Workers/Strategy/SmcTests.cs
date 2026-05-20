using StrategyWorker.Engine;
using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// SMC（Smart Money Concepts）indicator + strategy 合約測試。
/// 用手刻的 K 線場景驗證結構偵測（BOS / Order Block / FVG）跟進場映射。
/// </summary>
public class SmcTests
{
    private static BarData B(int i, decimal o, decimal h, decimal l, decimal c) => new()
    {
        OpenTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i),
        Open = o, High = h, Low = l, Close = c, Volume = 1_000_000,
    };

    /// <summary>
    /// 多頭場景（前置 24 根平盤暖機湊滿 strategy MinBars=40；平盤只產生 high pivot、無 low pivot，
    /// 所以在真正的 swing low 形成前不會有 break，結構乾淨）：
    /// 之後 swing high(106) → swing low(98) → 紅 K OB 區 [102.5,104.5] → 綠 K close 108 突破 106 (BOS_Up)
    /// → 價回落到 103.5（落在 OB 區內）→ 應 buy / OB_Retest。
    /// </summary>
    private static List<BarData> BullishBosObRetest()
    {
        var bars = new List<BarData>();
        int i = 0;
        for (; i < 24; i++) bars.Add(B(i, 100, 100.5m, 99.5m, 100));  // 平盤暖機

        bars.Add(B(i++, 100, 101,    99,     100));
        bars.Add(B(i++, 101, 102,    100,    101));
        bars.Add(B(i++, 103, 104,    102,    103));
        bars.Add(B(i++, 105, 106,    104,    105));   // swing high 106
        bars.Add(B(i++, 103, 104,    102,    103));
        bars.Add(B(i++, 101, 102,    100,    101));
        bars.Add(B(i++, 100, 101,    99,     100));
        bars.Add(B(i++, 100, 101,    99,     100));
        bars.Add(B(i++, 99,  100,    98,     99));    // swing low 98
        bars.Add(B(i++, 100, 101,    99,     100));
        bars.Add(B(i++, 101, 102,    100,    101));
        bars.Add(B(i++, 102, 103,    101,    102));
        bars.Add(B(i++, 104, 104.5m, 102.5m, 103));   // red OB candle, zone [102.5, 104.5]
        bars.Add(B(i++, 103, 108.5m, 103,    108));   // green, BOS_Up (close 108 > swing high 106)
        bars.Add(B(i++, 108, 108.5m, 105.5m, 106));
        bars.Add(B(i++, 105, 105.5m, 104.5m, 105));
        bars.Add(B(i++, 104, 104.5m, 103.5m, 104));
        bars.Add(B(i++, 104, 104.5m, 103,    103.5m));// pullback into OB zone
        return bars;
    }

    [Fact]
    public void Detect_BullishBosObRetest_TrendUpAndBuy()
    {
        var st = Smc.Detect(BullishBosObRetest());

        st.Trend.Should().Be("up");
        st.BreakType.Should().Be("BOS_Up");
        st.Signal.Should().Be("buy");
        st.SignalType.Should().Be("OB_Retest");
        st.ActiveBullObCount.Should().BeGreaterThanOrEqualTo(1);
        st.ZoneLow.Should().Be(102.5m);
        st.ZoneHigh.Should().Be(104.5m);
    }

    [Fact]
    public void Strategy_BullishScenario_EmitsBuyWithConfidence()
    {
        var strat = new SmcStrategy();
        var cfg = new StrategyConfig { Symbol = "BTC-USDT", Exchange = "bingx", Interval = "1h" };

        var sig = strat.Evaluate(BullishBosObRetest(), cfg);

        sig.Action.Should().Be("buy");
        sig.Confidence.Should().BeGreaterThan(0.5m);
        sig.Strategy.Should().Be("smc");
        sig.Indicators["trend"].Should().Be(1m);
        sig.Indicators["break_type"].Should().Be(1m);        // BOS_Up
        sig.Indicators["signal_type"].Should().Be(1m);       // OB_Retest
    }

    [Fact]
    public void Detect_BullishFvg_CountedActive()
    {
        // bar7.High(100.5) < bar9.Low(102) → bullish FVG，之後沒被 close 跌破 → active
        var bars = new List<BarData>
        {
            B(0, 100, 100.5m, 99.5m, 100),
            B(1, 100, 100.5m, 99.5m, 100),
            B(2, 100, 100.5m, 99.5m, 100),
            B(3, 100, 100.5m, 99.5m, 100),
            B(4, 100, 100.5m, 99.5m, 100),
            B(5, 100, 100.5m, 99.5m, 100),
            B(6, 100, 100.5m, 99.5m, 100),
            B(7, 100, 100.5m, 99.5m, 100),
            B(8, 101, 101.5m, 100.8m, 101),
            B(9, 102.5m, 103, 102, 102.5m),   // gap up: bar7.High 100.5 < bar9.Low 102
            B(10, 102.5m, 103, 102, 102.5m),
            B(11, 102.5m, 103, 102, 102.5m),
            B(12, 102.5m, 103, 102, 102.5m),
        };

        var st = Smc.Detect(bars);
        st.ActiveBullFvgCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Detect_FlatSeries_NoStructureHold()
    {
        var bars = new List<BarData>();
        for (int i = 0; i < 14; i++) bars.Add(B(i, 100, 100.5m, 99.5m, 100));

        var st = Smc.Detect(bars);
        st.Trend.Should().Be("neutral");
        st.Signal.Should().Be("hold");
    }

    [Fact]
    public void Strategy_InsufficientBars_ReturnsHold()
    {
        var strat = new SmcStrategy();
        var cfg = new StrategyConfig { Symbol = "BTC-USDT", Exchange = "bingx" };
        var bars = new List<BarData>();
        for (int i = 0; i < 10; i++) bars.Add(B(i, 100, 101, 99, 100));

        var sig = strat.Evaluate(bars, cfg);
        sig.Action.Should().Be("hold");
        sig.Confidence.Should().Be(0m);
    }

    [Fact]
    public void Detect_EmptyOrTiny_NeutralHold()
    {
        Smc.Detect(new List<BarData>()).Signal.Should().Be("hold");
        Smc.Detect(new List<BarData> { B(0, 100, 101, 99, 100) }).Trend.Should().Be("neutral");
    }
}
