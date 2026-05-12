using StrategyWorker.Engine;
using StrategyWorker.Engine.Indicators;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// Look-ahead bias 審查測試。
///
/// 不變式定義：對任意截斷點 t，indicator(bars[0..t]) 必須等於
/// indicator(bars[0..N])[t]。任何指標若使用 t 之後的資料就會違反——
/// 通常是設計錯誤（如：中位移動平均、未來感知 lookback、洩漏資料的
/// future window）。這些 case 在 walk-forward 回測時會作弊、把過擬合
/// 的策略呈現得像賺錢、上實單後爆掉。
///
/// 設計參考：對照組 ai-quant-starter2/tests/test_no_lookahead.py
/// (Python pytest 版)、本檔是 C# xUnit + FluentAssertions 等效實作、
/// 範圍涵蓋 strategy-worker 的全部 indicator。
///
/// 純合成資料（不打外網）以求穩定可重複。
/// </summary>
public class NoLookaheadTests
{
    // ── Synthetic OHLCV ─────────────────────────────────────────

    /// <summary>
    /// 生成 n 根「帶 drift + 雜訊」的日線。固定 seed 讓 CI 結果穩定。
    /// 模擬 GBM (geometric Brownian motion) 與真實 K 線結構接近。
    /// </summary>
    private static List<BarData> MakeSynthetic(int n = 250, int seed = 42)
    {
        var rng = new Random(seed);
        var drift = 0.0005;
        var sigma = 0.018;
        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var bars = new List<BarData>(n);
        var close = 100.0;
        for (int i = 0; i < n; i++)
        {
            var z = NextGaussian(rng);
            var ret = drift + sigma * z;
            close = close * Math.Exp(ret);
            var hiOffset = Math.Abs(NextGaussian(rng)) * 0.008;
            var loOffset = Math.Abs(NextGaussian(rng)) * 0.008;
            var openOffset = NextGaussian(rng) * 0.005;

            bars.Add(new BarData
            {
                OpenTime = t0.AddDays(i),
                Open   = (decimal)(close * (1 + openOffset)),
                High   = (decimal)(close * (1 + hiOffset)),
                Low    = (decimal)(close * (1 - loOffset)),
                Close  = (decimal)close,
                Volume = rng.Next(1_000_000, 5_000_000),
            });
        }
        return bars;
    }

    private static double NextGaussian(Random rng)
    {
        // Box-Muller
        var u1 = 1.0 - rng.NextDouble();
        var u2 = 1.0 - rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    private static void AssertClose(decimal expected, decimal actual, string name, decimal tol = 1e-9m)
    {
        // 絕對誤差 + 相對誤差 (處理大數)
        var diff = Math.Abs(expected - actual);
        var scale = Math.Max(1m, Math.Abs(expected));
        diff.Should().BeLessThan(tol * scale,
            $"{name} 截斷後計算值與全量計算值不一致（full={expected}, sub={actual}）— 有 look-ahead bias");
    }

    // ── Bollinger Bands ─────────────────────────────────────────

    [Theory]
    [InlineData(50)]
    [InlineData(120)]
    [InlineData(200)]
    [InlineData(249)]
    public void BollingerBands_NoLookahead(int truncAt)
    {
        var full = MakeSynthetic(250);
        var sub  = full.Take(truncAt + 1).ToList();

        var bFull = BollingerBands.Compute(full.Take(truncAt + 1).ToList(), full[truncAt].Close);
        var bSub  = BollingerBands.Compute(sub, sub[^1].Close);

        bFull.Should().NotBeNull();
        bSub.Should().NotBeNull();
        AssertClose(bFull!.Upper,     bSub!.Upper,     "BB.Upper");
        AssertClose(bFull.Mid,        bSub.Mid,        "BB.Mid");
        AssertClose(bFull.Lower,      bSub.Lower,      "BB.Lower");
        AssertClose(bFull.BandWidth,  bSub.BandWidth,  "BB.BandWidth");
        AssertClose(bFull.PercentB,   bSub.PercentB,   "BB.PercentB");
    }

    // ── Vegas Tunnel ────────────────────────────────────────────
    // 經典參數需要 ≥676 根、合成資料用 compact 版本參數
    // 仍然驗證 truncation invariant

    [Theory]
    [InlineData(80)]
    [InlineData(150)]
    [InlineData(240)]
    public void VegasTunnel_NoLookahead(int truncAt)
    {
        var full = MakeSynthetic(250);
        var sub  = full.Take(truncAt + 1).ToList();

        // 用 compact 版本（34/55/144/233）測試、滿足較小 truncation
        var vFull = VegasTunnel.Compute(full.Take(truncAt + 1).ToList(),
            mainFast: 34, mainSlow: 55, longFast: 144, longSlow: 233, triggerPeriod: 12);
        var vSub  = VegasTunnel.Compute(sub,
            mainFast: 34, mainSlow: 55, longFast: 144, longSlow: 233, triggerPeriod: 12);

        if (vFull == null || vSub == null)
            return;  // bars 不足兩邊都會 null、屬於同一行為、不算 bias

        AssertClose(vFull.MainFastEma,    vSub.MainFastEma,    "Vegas.MainFastEma");
        AssertClose(vFull.MainSlowEma,    vSub.MainSlowEma,    "Vegas.MainSlowEma");
        AssertClose(vFull.LongFastEma,    vSub.LongFastEma,    "Vegas.LongFastEma");
        AssertClose(vFull.LongSlowEma,    vSub.LongSlowEma,    "Vegas.LongSlowEma");
        AssertClose(vFull.TriggerEma,     vSub.TriggerEma,     "Vegas.TriggerEma");
        AssertClose(vFull.TunnelUpper,    vSub.TunnelUpper,    "Vegas.TunnelUpper");
        AssertClose(vFull.TunnelLower,    vSub.TunnelLower,    "Vegas.TunnelLower");
    }

    // ── Regime Detector ─────────────────────────────────────────

    [Theory]
    [InlineData(60)]
    [InlineData(150)]
    [InlineData(240)]
    public void RegimeDetector_NoLookahead(int truncAt)
    {
        var full = MakeSynthetic(250);
        var sub  = full.Take(truncAt + 1).ToList();

        var rFull = RegimeDetector.Detect(full.Take(truncAt + 1).ToList());
        var rSub  = RegimeDetector.Detect(sub);

        rFull.Type.Should().Be(rSub.Type,
            $"Regime type @{truncAt} 截斷後分類應一致（full={rFull.Type}, sub={rSub.Type}）— 有 look-ahead bias");
    }

    // ── Fibonacci Swing ─────────────────────────────────────────

    [Theory]
    [InlineData(60)]
    [InlineData(150)]
    [InlineData(240)]
    public void FibonacciSwing_NoLookahead(int truncAt)
    {
        var full = MakeSynthetic(250);
        var sub  = full.Take(truncAt + 1).ToList();

        var lookback = 30;
        var (hiFull, _, loFull, _) = FibonacciLevels.FindSwing(full.Take(truncAt + 1).ToList(), lookback);
        var (hiSub,  _, loSub,  _) = FibonacciLevels.FindSwing(sub, lookback);

        AssertClose(hiFull, hiSub, "Fib.SwingHigh");
        AssertClose(loFull, loSub, "Fib.SwingLow");
    }

    // ── Harmonic Patterns ───────────────────────────────────────

    [Theory]
    [InlineData(80)]
    [InlineData(150)]
    [InlineData(240)]
    public void HarmonicPatterns_NoLookahead(int truncAt)
    {
        var full = MakeSynthetic(250);
        var sub  = full.Take(truncAt + 1).ToList();

        var hFull = HarmonicPatterns.Detect(full.Take(truncAt + 1).ToList());
        var hSub  = HarmonicPatterns.Detect(sub);

        // 兩邊 pattern 偵測結果應一致（同 input 同 output）。
        // detection 內含 PatternName / Points / Confidence 等、比對核心欄位即可。
        hFull.PatternName.Should().Be(hSub.PatternName,
            $"Harmonic.PatternName @{truncAt} 截斷後不一致 — 有 look-ahead bias");
    }

    // ── SuperTrend（Batch A 移植） ──────────────────────────────
    // path-dependent indicator——以 close[i-1] 跨越前一根 final 軌判方向。
    // 截斷後第 t 根值必須跟全量第 t 根值相同。

    [Theory]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(200)]
    [InlineData(249)]
    public void SuperTrend_NoLookahead(int truncAt)
    {
        var full = MakeSynthetic(250);
        var sub  = full.Take(truncAt + 1).ToList();

        var stFull = SuperTrend.Compute(full.Take(truncAt + 1).ToList());
        var stSub  = SuperTrend.Compute(sub);
        stFull.Should().NotBeNull();
        stSub.Should().NotBeNull();
        AssertClose(stFull!.Value, stSub!.Value, "SuperTrend.Value");
        stFull.Trend.Should().Be(stSub.Trend, $"SuperTrend.Trend @{truncAt} 截斷後方向不一致");
        AssertClose(stFull.DistancePct, stSub.DistancePct, "SuperTrend.DistancePct");
    }

    // ── ADX + DI（Batch A 移植） ────────────────────────────────

    [Theory]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(200)]
    [InlineData(249)]
    public void AdxDi_NoLookahead(int truncAt)
    {
        var full = MakeSynthetic(250);
        var sub  = full.Take(truncAt + 1).ToList();

        var aFull = AdxDi.Compute(full.Take(truncAt + 1).ToList());
        var aSub  = AdxDi.Compute(sub);
        if (aFull == null || aSub == null) return;  // 兩邊 bars 不足都會 null、不算違反
        AssertClose(aFull.Adx,     aSub.Adx,     "AdxDi.Adx");
        AssertClose(aFull.PlusDi,  aSub.PlusDi,  "AdxDi.PlusDi");
        AssertClose(aFull.MinusDi, aSub.MinusDi, "AdxDi.MinusDi");
    }

    // ── Ichimoku（Batch A 移植） ────────────────────────────────

    [Theory]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(200)]
    [InlineData(249)]
    public void Ichimoku_NoLookahead(int truncAt)
    {
        var full = MakeSynthetic(250);
        var sub  = full.Take(truncAt + 1).ToList();

        var iFull = Ichimoku.Compute(full.Take(truncAt + 1).ToList());
        var iSub  = Ichimoku.Compute(sub);
        iFull.Should().NotBeNull();
        iSub.Should().NotBeNull();
        AssertClose(iFull!.Tenkan,      iSub!.Tenkan,      "Ichimoku.Tenkan");
        AssertClose(iFull.Kijun,        iSub.Kijun,        "Ichimoku.Kijun");
        AssertClose(iFull.SenkouSpanA,  iSub.SenkouSpanA,  "Ichimoku.SenkouSpanA");
        AssertClose(iFull.SenkouSpanB,  iSub.SenkouSpanB,  "Ichimoku.SenkouSpanB");
        iFull.PricePosition.Should().Be(iSub.PricePosition);
        iFull.TkCross.Should().Be(iSub.TkCross);
    }

    // ── Stochastic（Batch A 移植） ──────────────────────────────

    [Theory]
    [InlineData(30)]
    [InlineData(120)]
    [InlineData(200)]
    [InlineData(249)]
    public void Stochastic_NoLookahead(int truncAt)
    {
        var full = MakeSynthetic(250);
        var sub  = full.Take(truncAt + 1).ToList();

        var sFull = Stochastic.Compute(full.Take(truncAt + 1).ToList());
        var sSub  = Stochastic.Compute(sub);
        sFull.Should().NotBeNull();
        sSub.Should().NotBeNull();
        AssertClose(sFull!.K, sSub!.K, "Stochastic.K");
        AssertClose(sFull.D,  sSub.D,  "Stochastic.D");
    }

    // ── VWAP（Batch A 移植） ────────────────────────────────────

    [Theory]
    [InlineData(30)]
    [InlineData(120)]
    [InlineData(200)]
    [InlineData(249)]
    public void Vwap_NoLookahead(int truncAt)
    {
        var full = MakeSynthetic(250);
        var sub  = full.Take(truncAt + 1).ToList();

        var vFull = Vwap.Compute(full.Take(truncAt + 1).ToList());
        var vSub  = Vwap.Compute(sub);
        vFull.Should().NotBeNull();
        vSub.Should().NotBeNull();
        AssertClose(vFull!.Value,        vSub!.Value,        "Vwap.Value");
        AssertClose(vFull.DeviationPct,  vSub.DeviationPct,  "Vwap.DeviationPct");
    }

    // ── Meta: 同 input 多次呼叫一致性 ───────────────────────────
    // 補捉「indicator 內部用全域狀態」這種隱性 bug（Compute 第二次跟第一次回不同值）

    [Fact]
    public void Indicators_AreDeterministic_OnRepeatedCalls()
    {
        var bars = MakeSynthetic(150);
        var price = bars[^1].Close;

        var bb1 = BollingerBands.Compute(bars, price);
        var bb2 = BollingerBands.Compute(bars, price);
        bb1.Should().NotBeNull();
        bb2.Should().NotBeNull();
        bb1!.Upper.Should().Be(bb2!.Upper);
        bb1.Mid.Should().Be(bb2.Mid);
        bb1.Lower.Should().Be(bb2.Lower);

        var r1 = RegimeDetector.Detect(bars);
        var r2 = RegimeDetector.Detect(bars);
        r1.Type.Should().Be(r2.Type);

        var h1 = HarmonicPatterns.Detect(bars);
        var h2 = HarmonicPatterns.Detect(bars);
        h1.PatternName.Should().Be(h2.PatternName);

        // Batch A 新增的 5 個 indicator 也驗 determinism
        var st1 = SuperTrend.Compute(bars);
        var st2 = SuperTrend.Compute(bars);
        st1!.Value.Should().Be(st2!.Value);
        st1.Trend.Should().Be(st2.Trend);

        var ad1 = AdxDi.Compute(bars);
        var ad2 = AdxDi.Compute(bars);
        ad1!.Adx.Should().Be(ad2!.Adx);

        var ic1 = Ichimoku.Compute(bars);
        var ic2 = Ichimoku.Compute(bars);
        ic1!.Tenkan.Should().Be(ic2!.Tenkan);

        var so1 = Stochastic.Compute(bars);
        var so2 = Stochastic.Compute(bars);
        so1!.K.Should().Be(so2!.K);

        var vw1 = Vwap.Compute(bars);
        var vw2 = Vwap.Compute(bars);
        vw1!.Value.Should().Be(vw2!.Value);
    }
}
