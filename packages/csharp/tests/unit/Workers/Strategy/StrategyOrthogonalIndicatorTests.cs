using FluentAssertions;
using StrategyWorker.Engine;
using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;
using Xunit;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// 正交基礎指標 + 策略測試:Hurst(性格)與 VolatilityRegime(波動率狀態)是要增加 ensemble
/// 區別性的「非方向」維度。重點驗證:資料不足回 null/hold、輸出落在合理域、
/// 持續性序列的 Hurst &gt; 反持續序列(方向性正確),以及兩個新策略可被優化器掃參。
/// </summary>
public class StrategyOrthogonalIndicatorTests
{
    // ── Hurst ────────────────────────────────────────────────────────────
    [Fact]
    public void Hurst_TooFewBars_ReturnsNull()
        => Hurst.Compute(FromReturns(AntiPersistentReturns(20, 1))).Should().BeNull();

    [Fact]
    public void Hurst_Value_InUnitRange()
    {
        var h = Hurst.Compute(FromReturns(PersistentReturns(220, 7)), 200);
        h.Should().NotBeNull();
        h!.Value.Should().BeInRange(0m, 1m);
    }

    [Fact]
    public void Hurst_Persistent_GreaterThan_AntiPersistent()
    {
        var hPersist = Hurst.Compute(FromReturns(PersistentReturns(220, 7)), 200);
        var hAnti    = Hurst.Compute(FromReturns(AntiPersistentReturns(220, 7)), 200);
        hPersist.Should().NotBeNull();
        hAnti.Should().NotBeNull();
        hPersist!.Value.Should().BeGreaterThan(hAnti!.Value);
    }

    // ── VolatilityRegime ─────────────────────────────────────────────────
    [Fact]
    public void VolatilityRegime_TooFewBars_ReturnsNull()
        => VolatilityRegime.Compute(FromReturns(PersistentReturns(50, 3))).Should().BeNull();

    [Fact]
    public void VolatilityRegime_Percentile_InUnitRange()
    {
        var vr = VolatilityRegime.Compute(FromReturns(PersistentReturns(160, 3)));
        vr.Should().NotBeNull();
        vr!.Value.Percentile.Should().BeInRange(0m, 1m);
        vr.Value.Atr.Should().BeGreaterThan(0m);
    }

    // ── ReturnDistribution ───────────────────────────────────────────────
    [Fact]
    public void ReturnDistribution_TooFewBars_ReturnsNull()
        => ReturnDistribution.Compute(FromReturns(AntiPersistentReturns(20, 1))).Should().BeNull();

    [Fact]
    public void ReturnDistribution_DetectsNegativeSkewAndFatTails()
    {
        var d = ReturnDistribution.Compute(FromReturns(CrashReturns(160)));
        d.Should().NotBeNull();
        d!.Value.Skew.Should().BeLessThan(0m);        // 偶發暴跌 → 負偏
        d.Value.Kurtosis.Should().BeGreaterThan(0m);  // 極端值 → 肥尾
    }

    // ── HurstStrategy ────────────────────────────────────────────────────
    [Fact]
    public void HurstStrategy_IsOptimizable()
        => new HurstStrategy().ParamSchema.Keys
            .Should().Contain(new[] { "hurst_lookback", "hurst_trend_th", "hurst_meanrev_th" });

    [Fact]
    public void HurstStrategy_TooFewBars_Holds()
        => new HurstStrategy().Evaluate(FromReturns(PersistentReturns(20, 7)), Cfg())
            .Action.Should().Be("hold");

    [Fact]
    public void HurstStrategy_ProducesValidSignal()
    {
        var sig = new HurstStrategy().Evaluate(FromReturns(PersistentReturns(220, 7)), Cfg());
        sig.Action.Should().BeOneOf("buy", "sell", "hold");
        sig.Confidence.Should().BeInRange(0m, 1m);
    }

    // ── VolatilityBreakoutStrategy ───────────────────────────────────────
    [Fact]
    public void VolatilityBreakoutStrategy_IsOptimizable()
        => new VolatilityBreakoutStrategy().ParamSchema.Keys
            .Should().Contain(new[] { "vol_squeeze_pct", "vol_breakout_lookback" });

    [Fact]
    public void VolatilityBreakoutStrategy_TooFewBars_Holds()
        => new VolatilityBreakoutStrategy().Evaluate(FromReturns(PersistentReturns(50, 3)), Cfg())
            .Action.Should().Be("hold");

    [Fact]
    public void VolatilityBreakoutStrategy_ProducesValidSignal()
    {
        var sig = new VolatilityBreakoutStrategy().Evaluate(FromReturns(PersistentReturns(220, 3)), Cfg());
        sig.Action.Should().BeOneOf("buy", "sell", "hold");
        sig.Confidence.Should().BeInRange(0m, 1m);
    }

    // ── helpers ──────────────────────────────────────────────────────────
    private static StrategyConfig Cfg() => new() { Symbol = "X", Exchange = "test", Interval = "4h" };

    /// <summary>把對數報酬序列累乘成 K 線(High/Low 圍著 close,讓 ATR 類指標也能算)。</summary>
    private static List<BarData> FromReturns(double[] rets, double start = 100.0)
    {
        var bars = new List<BarData>();
        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        double price = start, prev = start;
        for (int i = 0; i <= rets.Length; i++)
        {
            if (i > 0) { prev = price; price *= Math.Exp(rets[i - 1]); }
            var close = (decimal)price;
            var open = (decimal)prev;
            bars.Add(new BarData
            {
                OpenTime = t0.AddHours(i * 4),
                Open = open,
                High = Math.Max(open, close) * 1.001m,
                Low  = Math.Min(open, close) * 0.999m,
                Close = close,
                Volume = 1_000_000m,
            });
        }
        return bars;
    }

    /// <summary>AR(1) 正自相關 + 微幅 drift → 持續性殘差(Hurst &gt; 0.5)。</summary>
    private static double[] PersistentReturns(int n, int seed)
    {
        var r = new double[n];
        double prev = 0.0;
        uint s = (uint)seed;
        for (int i = 0; i < n; i++)
        {
            s = s * 1664525u + 1013904223u;
            double noise = ((s >> 8) / (double)(1 << 24)) - 0.5;  // [-0.5, 0.5)
            prev = 0.75 * prev + 0.25 * noise * 0.02;
            r[i] = prev + 0.0005;
        }
        return r;
    }

    /// <summary>嚴格正負交替的報酬 → 反持續殘差(Hurst &lt; 0.5)。</summary>
    private static double[] AntiPersistentReturns(int n, int seed)
    {
        var r = new double[n];
        uint s = (uint)seed;
        for (int i = 0; i < n; i++)
        {
            s = s * 1664525u + 1013904223u;
            double mag = 0.005 + 0.01 * ((s >> 8) / (double)(1 << 24));  // 0.5%..1.5%
            r[i] = (i % 2 == 0 ? 1 : -1) * mag;
        }
        return r;
    }

    /// <summary>平時小漲、週期性暴跌 → 負偏 + 肥尾(尾部風險典型形狀)。</summary>
    private static double[] CrashReturns(int n)
    {
        var r = new double[n];
        for (int i = 0; i < n; i++) r[i] = 0.004;
        for (int i = 9; i < n; i += 12) r[i] = -0.06;
        return r;
    }
}
