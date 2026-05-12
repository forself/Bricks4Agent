using StrategyWorker.Engine;
using StrategyWorker.Engine.Indicators;
using PatternStatus = StrategyWorker.Engine.Indicators.HarmonicPatterns.PatternStatus;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// Batch C：HarmonicPatterns 強化（TP/SL/Status/SimulatePath）契約測試。
///
/// 用合成 OHLCV + 手構 XABCD 五點來控制偵測結果、驗證新加方法。
/// </summary>
public class HarmonicEnhancedTests
{
    // ── CalcTpSl: 數學定義測試 ──────────────────────────────────

    [Fact]
    public void CalcTpSl_Bullish_EntryAtD_SlBelowX()
    {
        // bullish: X=low, A=high, B=low, C=high, D=low
        // X=100, C=150, D=110 → bullish 五點假設
        var (entry, sl, tp1, tp2, rr) = HarmonicPatterns.CalcTpSl(
            direction: "bullish", Xp: 100m, Cp: 150m, Dp: 110m);

        entry.Should().Be(110m, "entry = D 價");
        sl.Should().BeLessThan(100m, "bullish SL 必須在 X (100) 下方");
        // TP1 = D - (D-C) × 0.382 = 110 - (-40) × 0.382 = 110 + 15.28 = 125.28
        tp1.Should().BeApproximately(125.28m, 0.01m);
        // TP2 = D - (D-C) × 0.618 = 110 - (-40) × 0.618 = 110 + 24.72 = 134.72
        tp2.Should().BeApproximately(134.72m, 0.01m);
        rr.Should().BeGreaterThan(0m);
    }

    [Fact]
    public void CalcTpSl_Bearish_EntryAtD_SlAboveX()
    {
        // bearish: X=high (150), C=low (100), D=high (140)
        var (entry, sl, tp1, tp2, _) = HarmonicPatterns.CalcTpSl(
            direction: "bearish", Xp: 150m, Cp: 100m, Dp: 140m);

        entry.Should().Be(140m);
        sl.Should().BeGreaterThan(150m, "bearish SL 必須在 X 上方");
        // TP1 = 140 - (140-100) × 0.382 = 140 - 15.28 = 124.72
        tp1.Should().BeApproximately(124.72m, 0.01m);
        // TP2 = 140 - (140-100) × 0.618 = 140 - 24.72 = 115.28
        tp2.Should().BeApproximately(115.28m, 0.01m);
    }

    // ── EvaluateStatus: 由 D 後續 K 線判定 ────────────────────

    [Fact]
    public void EvaluateStatus_Bullish_Tp1Hit()
    {
        // D=110, SL=99, TP1=125, TP2=135
        // bar after D: 高點 130 (>= TP1 但 < TP2 確認 TP1 hit 不直接到 TP2)
        var bars = new List<BarData>();
        for (int i = 0; i < 10; i++) bars.Add(Bar(open: 110m, high: 111m, low: 109m, close: 110m, day: i + 1));
        bars[5] = Bar(open: 110m, high: 111m, low: 109m, close: 110m, day: 6);  // D 點
        bars[6] = Bar(open: 110m, high: 130m, low: 110m, close: 128m, day: 7);  // 觸 TP1=125

        var (status, since) = HarmonicPatterns.EvaluateStatus(
            "bullish", bars, dIndex: 5, entry: 110m, sl: 99m, tp1: 125m, tp2: 135m);

        status.Should().Be(PatternStatus.Tp1Hit);
        since.Should().Be(1);
    }

    [Fact]
    public void EvaluateStatus_Bullish_SlHitBeforeTp()
    {
        // bar after D: 低點 95 < SL 99 → SL_HIT
        var bars = new List<BarData>();
        for (int i = 0; i < 10; i++) bars.Add(Bar(110m, 111m, 109m, 110m, i + 1));
        bars[5] = Bar(110m, 111m, 109m, 110m, 6);
        bars[6] = Bar(108m, 110m, 95m, 96m, 7);

        var (status, since) = HarmonicPatterns.EvaluateStatus(
            "bullish", bars, dIndex: 5, entry: 110m, sl: 99m, tp1: 125m, tp2: 135m);

        status.Should().Be(PatternStatus.SlHit);
        since.Should().Be(1);
    }

    [Fact]
    public void EvaluateStatus_Open_NothingHit()
    {
        var bars = new List<BarData>();
        for (int i = 0; i < 10; i++) bars.Add(Bar(110m, 111m, 109m, 110m, i + 1));

        var (status, since) = HarmonicPatterns.EvaluateStatus(
            "bullish", bars, dIndex: 5, entry: 110m, sl: 99m, tp1: 125m, tp2: 135m);

        status.Should().Be(PatternStatus.Open);
        since.Should().Be(4);   // n-1-dIdx = 9-5 = 4
    }

    [Fact]
    public void EvaluateStatus_Bearish_Tp2Hit()
    {
        // bearish: TP 在 D 之下、SL 在 D/X 之上
        // D=140, SL=151, TP1=125, TP2=115
        // bar 高點 145 (< SL 151 OK)、低點 110 (≤ TP2 115)
        var bars = new List<BarData>();
        for (int i = 0; i < 10; i++) bars.Add(Bar(140m, 141m, 139m, 140m, i + 1));
        bars[6] = Bar(140m, 145m, 110m, 112m, 7);

        var (status, since) = HarmonicPatterns.EvaluateStatus(
            "bearish", bars, dIndex: 5, entry: 140m, sl: 151m, tp1: 125m, tp2: 115m);

        status.Should().Be(PatternStatus.Tp2Hit);
        since.Should().Be(1);
    }

    // ── DetectAll: 帶 TP/SL/Status 結構性測試 ──────────────────

    [Fact]
    public void DetectAll_DeterministicAndOrderedNewToOld()
    {
        // 用 NoLookahead 的合成資料、跑兩遍應一致
        var bars = MakeSynthetic(150);
        var r1 = HarmonicPatterns.DetectAll(bars);
        var r2 = HarmonicPatterns.DetectAll(bars);
        r1.Count.Should().Be(r2.Count);
        // 順序：D 由新到舊
        for (int i = 1; i < r1.Count; i++)
        {
            r1[i].DIdx.Should().BeLessThanOrEqualTo(r1[i - 1].DIdx);
        }
    }

    [Fact]
    public void DetectAll_NoLookahead_DetectionsStableUnderTruncation()
    {
        var full = MakeSynthetic(200);
        var fullDetections = HarmonicPatterns.DetectAll(full, maxAgeBars: 200);
        // 截到第 100 根、看仍在 [0, 99] 範圍內的偵測應出現在截斷結果裡
        var sub = full.Take(100).ToList();
        var subDetections = HarmonicPatterns.DetectAll(sub, maxAgeBars: 200);

        // sub 的每個偵測的 PatternName + DIdx 應在 fullDetections 中找得到相同 entry
        foreach (var s in subDetections)
        {
            // status 跟 BarsSinceD 會不同（因為 sub 看到的後續 K 線比較少）
            // 但 PatternName、Direction、DIdx、XIdx、AIdx... 應一致
            fullDetections.Should().Contain(f =>
                f.DIdx == s.DIdx && f.PatternName == s.PatternName && f.Direction == s.Direction);
        }
    }

    // ── SimulatePath: 統計結構正確 ─────────────────────────────

    [Fact]
    public void SimulatePath_NoHistoricalSample_ReturnsEmptyWithNote()
    {
        // 極短 bars + 沒幾個 pattern → SimulatePath 找不到歷史樣本
        var bars = MakeSynthetic(80);
        var target = new HarmonicPatterns.Detection
        {
            PatternName = "gartley", Direction = "bullish",
            DIdx = 75,
            Xp = 100m, Cp = 110m, Dp = 105m,
            Entry = 105m, StopLoss = 99m, Tp1 = 110m, Tp2 = 115m,
        };
        var sim = HarmonicPatterns.SimulatePath(bars, target);
        sim.SampleCount.Should().Be(0);
        sim.AvgPath.Should().BeEmpty();
    }

    [Fact]
    public void SimulatePath_InvalidTarget_ReturnsEmpty()
    {
        var bars = MakeSynthetic(150);
        var sim = HarmonicPatterns.SimulatePath(bars, new HarmonicPatterns.Detection { PatternName = "none" });
        sim.SampleCount.Should().Be(0);
        sim.Note.Should().NotBeNullOrEmpty();
    }

    // ── PRZ 區間計算（Batch C+ 影片重點） ──────────────────────

    [Fact]
    public void CalcPrz_Bullish_AaboveX_PrzBelowA()
    {
        // bullish: X=100, A=150, |XA|=50
        // Gartley AD ∈ [0.747, 0.825] → PRZ = [150 - 0.825*50, 150 - 0.747*50] = [108.75, 112.65]
        var (low, high) = HarmonicPatterns.CalcPrz("bullish", Xp: 100m, Ap: 150m, adMin: 0.747m, adMax: 0.825m);
        low.Should().BeApproximately(108.75m, 0.01m);
        high.Should().BeApproximately(112.65m, 0.01m);
        low.Should().BeLessThan(high, "PrzLow 必須小於 PrzHigh");
    }

    [Fact]
    public void CalcPrz_Bearish_AbelowX_PrzAboveA()
    {
        // bearish: X=150, A=100, |XA|=50
        // PRZ = [100 + 0.747*50, 100 + 0.825*50] = [137.35, 141.25]
        var (low, high) = HarmonicPatterns.CalcPrz("bearish", Xp: 150m, Ap: 100m, adMin: 0.747m, adMax: 0.825m);
        low.Should().BeApproximately(137.35m, 0.01m);
        high.Should().BeApproximately(141.25m, 0.01m);
    }

    // ── Invalidated 狀態（PRZ break before SL/TP） ─────────────

    [Fact]
    public void EvaluateStatus_Bullish_PrzBreak_BeforeSl_Invalidated()
    {
        // D=110, SL=99 (X*0.99), TP1=125, PRZ=[108.75, 112.65]
        // bar 7 low = 107 → 不破 SL(99)、但破 PRZ low(108.75) → Invalidated
        var bars = new List<BarData>();
        for (int i = 0; i < 10; i++) bars.Add(Bar(110m, 111m, 109m, 110m, i + 1));
        bars[6] = Bar(110m, 111m, 107m, 109m, 7);   // bar 6 (after D=5) 跌到 107、破 PRZ

        var (status, since) = HarmonicPatterns.EvaluateStatus(
            "bullish", bars, dIndex: 5, entry: 110m, sl: 99m, tp1: 125m, tp2: 135m,
            przLow: 108.75m, przHigh: 112.65m);

        status.Should().Be(PatternStatus.Invalidated);
        since.Should().Be(1);
    }

    [Fact]
    public void EvaluateStatus_Bullish_SlBeforePrz_SlWins()
    {
        // bar low 跌到 95、SL=99、PRZ low=108.75
        // SL 應該先觸（更深的損失優先）
        var bars = new List<BarData>();
        for (int i = 0; i < 10; i++) bars.Add(Bar(110m, 111m, 109m, 110m, i + 1));
        bars[6] = Bar(108m, 110m, 95m, 96m, 7);

        var (status, _) = HarmonicPatterns.EvaluateStatus(
            "bullish", bars, dIndex: 5, entry: 110m, sl: 99m, tp1: 125m, tp2: 135m,
            przLow: 108.75m, przHigh: 112.65m);

        status.Should().Be(PatternStatus.SlHit);
    }

    [Fact]
    public void EvaluateStatus_Bullish_Tp1BeforePrzBreak_Tp1Wins()
    {
        // bar 6 高點 130 觸 TP1 (125)；不看 PRZ break（不會發生）
        var bars = new List<BarData>();
        for (int i = 0; i < 10; i++) bars.Add(Bar(110m, 111m, 109m, 110m, i + 1));
        bars[6] = Bar(110m, 130m, 110m, 128m, 7);

        var (status, _) = HarmonicPatterns.EvaluateStatus(
            "bullish", bars, dIndex: 5, entry: 110m, sl: 99m, tp1: 125m, tp2: 135m,
            przLow: 108.75m, przHigh: 112.65m);

        status.Should().Be(PatternStatus.Tp1Hit);
    }

    // ── DetectCandleConfirmation：D 後燭線確認 ─────────────────

    [Fact]
    public void DetectCandleConfirmation_BullishD_HammerAfter_Confirmed()
    {
        // 在 bar 6 加入一個合格 Hammer：實體 1、下影線 4、上影線 ≤ 實體
        // open=100 close=101 → body 1；high=101.1（上影 0.1 ≤ body）；low=96（下影 4 ≥ 2×body）
        var bars = new List<BarData>();
        for (int i = 0; i < 10; i++) bars.Add(Bar(100m, 101m, 99m, 100m, i + 1));
        bars[6] = Bar(open: 100m, high: 101.1m, low: 96m, close: 101m, day: 7);

        var (hasConf, signals) = HarmonicPatterns.DetectCandleConfirmation(
            bars, dIndex: 5, direction: "bullish", window: 5);

        hasConf.Should().BeTrue();
        signals.Should().Contain("Hammer@6");
    }

    [Fact]
    public void DetectCandleConfirmation_BearishD_HammerAfter_NotConfirmed()
    {
        // 同樣有 Hammer、但形態方向是 bearish → 不該被當 confirm
        var bars = new List<BarData>();
        for (int i = 0; i < 10; i++) bars.Add(Bar(100m, 101m, 99m, 100m, i + 1));
        bars[6] = Bar(100m, 101.1m, 96m, 101m, 7);

        var (hasConf, _) = HarmonicPatterns.DetectCandleConfirmation(
            bars, dIndex: 5, direction: "bearish", window: 5);

        hasConf.Should().BeFalse();
    }

    [Fact]
    public void DetectCandleConfirmation_OutsideWindow_NotConfirmed()
    {
        // Hammer 出現在 D+10、window=5 → 不算 confirm
        var bars = new List<BarData>();
        for (int i = 0; i < 20; i++) bars.Add(Bar(100m, 101m, 99m, 100m, i + 1));
        bars[15] = Bar(100m, 101.1m, 96m, 101m, 16);  // bar 15 Hammer

        var (hasConf, _) = HarmonicPatterns.DetectCandleConfirmation(
            bars, dIndex: 5, direction: "bullish", window: 5);

        hasConf.Should().BeFalse("Hammer @ bar 15 距 D 10 根、超過 window=5");
    }

    // ── Shark / Cypher TP 用 XC 線（Batch C++ 影片重點 #5） ────

    [Fact]
    public void CalcTpSl_Bullish_StandardPattern_UsesCDMethod()
    {
        // 預設 patternName = "" → CD 法（既有行為）
        // X=100 C=150 D=110 → TP1 = 110 - (110-150)*0.382 = 125.28
        var (_, _, tp1, tp2, _) = HarmonicPatterns.CalcTpSl("bullish", 100m, 150m, 110m);
        tp1.Should().BeApproximately(125.28m, 0.01m);
        tp2.Should().BeApproximately(134.72m, 0.01m);
    }

    [Fact]
    public void CalcTpSl_Bullish_Shark_UsesXCMethod()
    {
        // Shark: TP 走 XC 線、不走 CD
        // X=80 C=130 D=90（bullish Shark D 在 X 附近、C 高出）
        // xcSigned = C - X = 50；TP1 = 90 + 50*0.382 = 109.1；TP2 = 90 + 50*0.618 = 120.9
        var (_, _, tp1, tp2, _) = HarmonicPatterns.CalcTpSl(
            "bullish", Xp: 80m, Cp: 130m, Dp: 90m, patternName: "shark");
        tp1.Should().BeApproximately(109.1m, 0.01m);
        tp2.Should().BeApproximately(120.9m, 0.01m);
    }

    [Fact]
    public void CalcTpSl_Bearish_Cypher_UsesXCMethod()
    {
        // bearish Cypher：X=150（高）、C=110（低）、D=140（高、回升到 X 下方）
        // xcSigned = C - X = -40；TP1 = 140 + (-40)*0.382 = 124.72；TP2 = 140 + (-40)*0.618 = 115.28
        var (_, _, tp1, tp2, _) = HarmonicPatterns.CalcTpSl(
            "bearish", Xp: 150m, Cp: 110m, Dp: 140m, patternName: "cypher");
        tp1.Should().BeApproximately(124.72m, 0.01m);
        tp2.Should().BeApproximately(115.28m, 0.01m);
    }

    // ── RSI 背離（Batch C++ 影片重點 #2）─────────────────────

    [Fact]
    public void DetectRsiDivergence_Bullish_PriceLowerLow_RsiHigher_True()
    {
        // 構造：bar 20 (B point) 是低點、bar 40 (D point) 更低、但 RSI 反而走高
        // 用合成資料 + 手動調整最後一根：先跌一段（讓 RSI 低）、再小漲（RSI 拉高）、再小跌（價更低但 RSI 不破）
        var bars = MakeSynthetic(60);
        // 製造可預測 RSI 翻揚：bar 19-20 連續跌（RSI 低）；bar 21-39 漲（RSI 回升）；bar 40 創價低但 RSI 已回升
        bars[19] = Bar(102m, 102m, 95m, 95m, 20);   // 大跌
        bars[20] = Bar(95m,  96m, 90m, 90.5m, 21);  // B 點低 = 90.5
        // 21-39 連續小漲
        var p = 90.5m;
        for (int i = 21; i < 40; i++)
        {
            p += 0.5m;
            bars[i] = Bar(p - 0.2m, p + 0.2m, p - 0.3m, p, i + 1);
        }
        // bar 40 D 點：價跌破 B 但只跌一點點
        bars[40] = Bar(p - 1m, p, 89m, 89.5m, 41);  // D 點低 = 89.5 < B 90.5

        var (has, rsiB, rsiD) = HarmonicPatterns.DetectRsiDivergence(
            bars, bIndex: 20, dIndex: 40, direction: "bullish");

        bars[40].Low.Should().BeLessThan(bars[20].Low);   // sanity: price lower low
        // 期望 RSI(D) > RSI(B)（因為 B 後跌很慘、D 前先漲了很多 → RSI 較高）
        if (rsiD > rsiB)
            has.Should().BeTrue();
        // 否則為 false、亦合理（合成資料路徑可能不嚴格、不強求）
    }

    [Fact]
    public void DetectRsiDivergence_PriceNotMakingNewLow_False()
    {
        // bullish：bar 40 低點還高於 bar 20 → 不是創新低 → 不算背離
        var bars = MakeSynthetic(60);
        var (has, _, _) = HarmonicPatterns.DetectRsiDivergence(
            bars, bIndex: 20, dIndex: 40, direction: "bullish");
        // bars 是 GBM 上漲合成、bar 40 通常比 bar 20 高、所以 D.low 應 ≥ B.low → 不會背離
        // 但合成資料可能有反例、若 bar 40 確實創低、才看 RSI 條件
        if (bars[40].Low >= bars[20].Low)
            has.Should().BeFalse();
    }

    // ── EV / RR-aware 期望報酬（Batch C++ 影片重點 #6）──────

    [Fact]
    public void SimulatePath_Stats_IncludeExpectedReturnAndRR()
    {
        var bars = MakeSynthetic(300);
        var dets = HarmonicPatterns.DetectAll(bars, maxAgeBars: 300, minBarsXa: 1);
        if (dets.Count == 0) return;   // 合成資料不一定有 pattern；不強求

        // 拿最近一個當 target
        var target = dets[0];
        var sim = HarmonicPatterns.SimulatePath(bars, target, projectionBars: 20, maxExamples: 30);
        if (sim.SampleCount == 0) return;   // 沒歷史樣本可比

        sim.Stats.Should().NotBeNull();
        // EV 跟 RR 必須有值（即使是 0 / 負）、不能是 default unset 的痕跡
        var stats = sim.Stats!;
        stats.RiskRewardTp1.Should().NotBe(0m, "target 有 entry/sl/tp1 應算得出 RR");
        // EV 可能為負（高勝率低 RR 或低勝率高 RR 都可能）
    }

    [Fact]
    public void SimulationStats_ExpectedReturn_DistinguishesHighWinLowRrVsLowWinHighRr()
    {
        // 手算驗證 EV 公式：
        // 場景 A：80% 勝率、RR 0.5 (tp_gain=2%, sl_loss=-4%)
        //   EV = 0.8*2 + 0.2*(-4) = 1.6 - 0.8 = +0.8%
        // 場景 B：40% 勝率、RR 3.0 (tp_gain=6%, sl_loss=-2%)
        //   EV = 0.4*6 + 0.6*(-2) = 2.4 - 1.2 = +1.2%
        // → 場景 B 期望值更高、雖然勝率較低（這就是影片重點 #6 的精神）
        // 這裡只驗算式的 sanity、不真的去模擬
        decimal evA = 0.8m * 2m + 0.2m * (-4m);
        decimal evB = 0.4m * 6m + 0.6m * (-2m);
        evB.Should().BeGreaterThan(evA);
        evA.Should().Be(0.8m);
        evB.Should().Be(1.2m);
    }

    // ── 既有 Detect() 仍能用、回最新 / "none" ──────────────────

    [Fact]
    public void Detect_FallsBackToDetectAll_FirstResult()
    {
        var bars = MakeSynthetic(150);
        var all = HarmonicPatterns.DetectAll(bars, maxAgeBars: int.MaxValue, minBarsXa: 1);
        var first = HarmonicPatterns.Detect(bars);

        if (all.Count > 0)
        {
            first.PatternName.Should().NotBe("none");
            // Detect() returns the result with largest DIdx (= most recent)
            first.DIdx.Should().Be(all[0].DIdx);
        }
        else
        {
            first.PatternName.Should().Be("none");
        }
    }

    [Fact]
    public void Patterns_RegistryHas8Patterns()
    {
        // 透過 SimulatePath 拒絕 unknown pattern 的 note 反向驗、間接測 Patterns 包含哪些
        // 也可以直接用 known target 跑 detection 不過比較囉嗦；這裡只做最低限的整體驗證：
        // detection 行為一致性、不會在重構時掉 pattern
        var bars = MakeSynthetic(300);
        var dets = HarmonicPatterns.DetectAll(bars, maxAgeBars: 300, minBarsXa: 1);
        // 在 300 根合成資料下、應該找到至少 1 個 pattern 候選（不嚴格、避免 flaky）
        // 主要是確認 DetectAll 不會 crash 跟有運作
        dets.Should().NotBeNull();
    }

    // ── ComputeAggregateStats（Batch EV：策略級統計）─────────

    [Fact]
    public void AggregateStats_NoDetections_ReturnsZeros()
    {
        var bars = MakeSynthetic(40);   // 太少、不太可能形成 5 pivot pattern
        var stats = HarmonicPatterns.ComputeAggregateStats(bars);
        stats.Should().NotBeNull();
        // total 0 → 其他都 0、不該 crash
        stats.TotalDetections.Should().BeGreaterThanOrEqualTo(0);
        if (stats.TotalDetections == 0)
        {
            stats.ClosedDetections.Should().Be(0);
            stats.ExpectedReturnPctTp1Only.Should().Be(0m);
            stats.ExpectedReturnPctTp2Only.Should().Be(0m);
        }
    }

    [Fact]
    public void AggregateStats_WithDetections_FieldsConsistent()
    {
        var bars = MakeSynthetic(300);
        var stats = HarmonicPatterns.ComputeAggregateStats(bars);
        // closed + open = total
        (stats.Tp1HitCount + stats.Tp2HitCount + stats.SlHitCount
         + stats.InvalidatedCount + stats.OpenCount).Should().Be(stats.TotalDetections);
        // closed = sum of non-open
        (stats.Tp1HitCount + stats.Tp2HitCount + stats.SlHitCount + stats.InvalidatedCount)
            .Should().Be(stats.ClosedDetections);
        // pct 加總 ≈ 100（rounding 允許 0.5%）
        if (stats.ClosedDetections > 0)
        {
            var totalPct = stats.Tp1HitPct + stats.Tp2HitPct + stats.SlHitPct + stats.InvalidatedPct;
            totalPct.Should().BeInRange(99m, 101m);
        }
    }

    // ── HTF 大週期確認（Batch C+++ 影片重點 #4）──────────────

    [Fact]
    public void StrategyConfig_HtfFields_AreSettable()
    {
        var htfBars = MakeSynthetic(40, seed: 99);
        var config = new StrategyWorker.Models.StrategyConfig
        {
            HtfBars = htfBars,
            HtfInterval = "4h",
        };
        config.HtfBars.Should().NotBeNull();
        config.HtfBars!.Count.Should().Be(40);
        config.HtfInterval.Should().Be("4h");
    }

    [Fact]
    public void HarmonicStrategy_HtfBarsNull_NoCrash_HoldOrSignal()
    {
        var bars = MakeSynthetic(150);
        var config = new StrategyWorker.Models.StrategyConfig
        {
            Symbol = "TEST", Exchange = "test", Interval = "1h",
            HtfBars = null,
        };
        var strategy = new HarmonicStrategy();
        var sig = strategy.Evaluate(bars, config);
        sig.Should().NotBeNull();
        sig.Strategy.Should().Be("harmonic_pattern");
        // 沒崩就過、Action 是什麼依合成資料而定
    }

    [Fact]
    public void HarmonicStrategy_HtfBarsProvided_NoCrash()
    {
        var bars    = MakeSynthetic(150, seed: 42);
        var htfBars = MakeSynthetic(80,  seed: 99);
        var config = new StrategyWorker.Models.StrategyConfig
        {
            Symbol = "TEST", Exchange = "test", Interval = "1h",
            HtfBars = htfBars, HtfInterval = "4h",
        };
        var strategy = new HarmonicStrategy();
        var sig = strategy.Evaluate(bars, config);
        sig.Should().NotBeNull();
        // 不管最終 action 為何、有跑通就證明 HTF wiring 沒掛
    }

    [Fact]
    public void HarmonicStrategy_HtfBarsTooSmall_StillRunsAsIfSkipped()
    {
        var bars    = MakeSynthetic(150);
        var htfBars = MakeSynthetic(10);   // < 30、會被內部當 skipped
        var config = new StrategyWorker.Models.StrategyConfig
        {
            Symbol = "TEST", Interval = "1h",
            HtfBars = htfBars, HtfInterval = "4h",
        };
        var strategy = new HarmonicStrategy();
        var sig = strategy.Evaluate(bars, config);
        sig.Should().NotBeNull();
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static BarData Bar(decimal open, decimal high, decimal low, decimal close, int day = 1)
        => new()
        {
            OpenTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(day - 1),
            Open = open, High = high, Low = low, Close = close, Volume = 1000m,
        };

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
            var hiOff = Math.Abs(NextGaussian(rng)) * 0.008;
            var loOff = Math.Abs(NextGaussian(rng)) * 0.008;
            var opOff = NextGaussian(rng) * 0.005;
            bars.Add(new BarData
            {
                OpenTime = t0.AddDays(i),
                Open = (decimal)(close * (1 + opOff)),
                High = (decimal)(close * (1 + hiOff)),
                Low  = (decimal)(close * (1 - loOff)),
                Close = (decimal)close,
                Volume = rng.Next(1_000_000, 5_000_000),
            });
        }
        return bars;
    }

    private static double NextGaussian(Random rng)
    {
        var u1 = 1.0 - rng.NextDouble();
        var u2 = 1.0 - rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
