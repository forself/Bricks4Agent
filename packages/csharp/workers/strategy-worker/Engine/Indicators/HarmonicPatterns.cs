using StrategyWorker.Models;

namespace StrategyWorker.Engine.Indicators;

/// <summary>
/// 諧波形態偵測器（Harmonic Patterns）。
///
/// 經典 5 點形態 XABCD：
///    X ──→ A ──→ B ──→ C ──→ D
///   由 Fibonacci 比率定義每段之間的比例。
///
/// 本版實作 8 種形態（Batch C 從朋友 ai-quant-starter2/harmonic_engine.py 擴充）：
///   Gartley / Bat / Butterfly / Crab / Deep_Crab / Deep_Gartley / Cypher / Shark。
///
/// 偵測有「看多」（bullish）跟「看空」（bearish）兩個方向——
///   bullish: X 低、A 高、B 低、C 高、D 低（D 進場買）
///   bearish: X 高、A 低、B 高、C 低、D 高（D 進場賣）
///
/// 實作步驟：
///   1. 用 fractal pivot 找 swing high/low
///   2. 依時序掃所有「嚴格交替 high/low」的 5 連窗
///   3. 計算 |AB|/|XA|、|BC|/|AB|、|CD|/|BC|、|AD|/|XA| 四個比率
///   4. 與每個形態的比率範圍比對（±15% 容忍）
///   5. 算 TP1/TP2/SL/RR + 從 D 點後續 K 線判 status（OPEN / TP1_HIT / TP2_HIT / SL_HIT）
///
/// 主要 API：
///   Detect(bars)           - 回傳最近一個（D 最新）的形態，無則 PatternName="none"
///   DetectAll(bars)        - 回傳所有未過期的形態列表、含 TP/SL/Status
///   SimulatePath(bars,tgt) - 拿歷史同型態樣本投影目標形態的未來路徑 + 命中率統計
///
/// 設計參考：Scott Carney《Harmonic Trading》+ 朋友 ai-quant-starter2/harmonic_engine.py。
/// </summary>
public static class HarmonicPatterns
{
    private const decimal TolerancePad = 0.15m;   // 真實 K 線比理論值寬鬆、容忍 ±15%

    public enum PatternStatus
    {
        Open,         // 形態剛形成、在 PRZ 區間內、未觸 TP/SL/失效
        Tp1Hit,       // 觸到第一目標
        Tp2Hit,       // 觸到第二目標
        SlHit,        // 觸到停損（硬性、超過 X 一段）
        Invalidated,  // 突破 PRZ 區間但未到 SL（軟性失效、影片提到的較早期退場訊號）
    }

    public class Pivot
    {
        public int Index { get; init; }
        public decimal Price { get; init; }
        public bool IsHigh { get; init; }   // true = swing high；false = swing low
    }

    public class Detection
    {
        // 形態資訊
        public string PatternName { get; init; } = "";
        public string Direction { get; init; } = "";       // bullish / bearish / none
        public decimal Confidence { get; init; }           // 0-1 fit score

        // XABCD 五點
        public int XIdx { get; init; }
        public int AIdx { get; init; }
        public int BIdx { get; init; }
        public int CIdx { get; init; }
        public int DIdx { get; init; }

        public decimal Xp { get; init; }
        public decimal Ap { get; init; }
        public decimal Bp { get; init; }
        public decimal Cp { get; init; }
        public decimal Dp { get; init; }

        // 比率
        public decimal AbRatio { get; init; }
        public decimal BcRatio { get; init; }
        public decimal CdRatio { get; init; }
        public decimal AdRatio { get; init; }

        // 進場 / 出場（Batch C 新增）
        public decimal Entry { get; init; }
        public decimal StopLoss { get; init; }
        public decimal Tp1 { get; init; }
        public decimal Tp2 { get; init; }
        public decimal RiskReward { get; init; }     // (tp1 - entry) / (entry - sl)（bullish）

        // 狀態（Batch C 新增）— 從 D 後續 K 線判定
        public PatternStatus Status { get; init; }
        public int BarsSinceD { get; init; }

        // PRZ 區間（Batch C+ 新增、影片提到「PRZ 是區間不是單點」）
        // 用 pattern 自己的 AD 比率範圍從 A 投影、bullish 兩值都在 D 附近、低值是「最深可接受 D」
        public decimal PrzLow { get; init; }
        public decimal PrzHigh { get; init; }

        // 確認進場訊號（Batch C+ 新增、影片提到 PRZ + 反轉 K 才進）
        // 掃 D 後 N 根 K 線、找與形態方向相符的 Hammer/Engulfing（呼叫 PriceActionPatterns）
        public bool HasCandleConfirmation { get; init; }
        public string ConfirmationSignals { get; init; } = "";    // "Hammer@45,BullEngulf@47" 之類
    }

    public class SimulationResult
    {
        public int SampleCount { get; init; }
        public IReadOnlyList<PathPoint> AvgPath { get; init; } = Array.Empty<PathPoint>();
        public SimulationStats? Stats { get; init; }
        public string Note { get; init; } = "";
    }

    public class PathPoint
    {
        public int Bar { get; init; }            // K 線位移（0 = D 那根）
        public decimal Ratio { get; init; }      // 平均 close / D close
        public decimal Price { get; init; }      // 對應到 target.Entry × ratio
        public int Samples { get; init; }
    }

    public class SimulationStats
    {
        public int Samples { get; init; }
        public decimal Tp1HitPct { get; init; }
        public decimal Tp2HitPct { get; init; }
        public decimal SlHitPct { get; init; }
        public decimal? AvgBarsToTp1 { get; init; }
        public decimal? AvgBarsToTp2 { get; init; }
        public decimal? AvgBarsToSl  { get; init; }
    }

    // ── Pivot 偵測（fractal：前後 window 都比這根低/高） ──────────

    public static List<Pivot> FindPivots(List<BarData> bars, int window = 3)
    {
        var pivots = new List<Pivot>();
        if (bars.Count < window * 2 + 1) return pivots;

        for (int i = window; i < bars.Count - window; i++)
        {
            bool isHigh = true, isLow = true;
            for (int j = i - window; j <= i + window; j++)
            {
                if (j == i) continue;
                if (bars[j].High >= bars[i].High) isHigh = false;
                if (bars[j].Low  <= bars[i].Low ) isLow  = false;
            }
            if (isHigh) pivots.Add(new Pivot { Index = i, Price = bars[i].High, IsHigh = true  });
            if (isLow)  pivots.Add(new Pivot { Index = i, Price = bars[i].Low,  IsHigh = false });
        }
        return pivots.OrderBy(p => p.Index).ToList();
    }

    // ── 8 種形態的 Fibonacci 比率定義（Batch C 從朋友擴充）────────

    private record PatternSpec(string Name,
        (decimal Min, decimal Max) Ab,
        (decimal Min, decimal Max) Bc,
        (decimal Min, decimal Max) Cd,
        (decimal Min, decimal Max) Ad);

    private static readonly PatternSpec[] Patterns = new[]
    {
        new PatternSpec("gartley",      (0.582m, 0.654m), (0.382m, 0.886m), (1.272m, 1.618m), (0.747m, 0.825m)),
        new PatternSpec("bat",          (0.382m, 0.500m), (0.382m, 0.886m), (1.618m, 2.618m), (0.841m, 0.931m)),
        new PatternSpec("butterfly",    (0.747m, 0.825m), (0.382m, 0.886m), (1.618m, 2.618m), (1.220m, 1.668m)),
        new PatternSpec("crab",         (0.382m, 0.618m), (0.382m, 0.886m), (2.240m, 3.618m), (1.538m, 1.698m)),
        // Batch C 新增
        new PatternSpec("deep_crab",    (0.841m, 0.931m), (0.382m, 0.886m), (2.000m, 3.618m), (1.538m, 1.698m)),
        new PatternSpec("deep_gartley", (0.841m, 0.931m), (0.382m, 0.886m), (1.272m, 1.618m), (0.747m, 0.825m)),
        new PatternSpec("cypher",       (0.382m, 0.618m), (1.130m, 1.414m), (0.382m, 0.886m), (0.747m, 0.825m)),
        new PatternSpec("shark",        (1.130m, 1.618m), (0.500m, 0.886m), (1.618m, 2.240m), (0.886m, 1.130m)),
    };

    // ── 比率區間檢查（朋友 _check_pattern 邏輯） ────────────────────

    private static (bool Ok, decimal Fit) CheckPattern(
        decimal abXa, decimal bcAb, decimal cdBc, decimal adXa, PatternSpec template)
    {
        bool InRange(decimal v, decimal lo, decimal hi) =>
            v >= lo * (1m - TolerancePad) && v <= hi * (1m + TolerancePad);

        if (!(InRange(abXa, template.Ab.Min, template.Ab.Max)
            && InRange(bcAb, template.Bc.Min, template.Bc.Max)
            && InRange(cdBc, template.Cd.Min, template.Cd.Max)
            && InRange(adXa, template.Ad.Min, template.Ad.Max)))
        {
            return (false, 0m);
        }

        decimal Deviation(decimal v, decimal lo, decimal hi)
        {
            var center = (lo + hi) / 2m;
            var halfRange = (hi - lo) / 2m + center * TolerancePad;
            if (halfRange == 0m) return 0m;
            return Math.Min(1m, Math.Abs(v - center) / halfRange);
        }

        var devs = new[]
        {
            Deviation(abXa, template.Ab.Min, template.Ab.Max),
            Deviation(bcAb, template.Bc.Min, template.Bc.Max),
            Deviation(cdBc, template.Cd.Min, template.Cd.Max),
            Deviation(adXa, template.Ad.Min, template.Ad.Max),
        };
        var avgDev = (devs[0] + devs[1] + devs[2] + devs[3]) / 4m;
        var fit = Math.Clamp(1m - avgDev, 0m, 1m);
        return (true, fit);
    }

    // ── 對單一 XABCD 跑分類 ─────────────────────────────────────

    private static Detection? ClassifyXabcd(Pivot X, Pivot A, Pivot B, Pivot C, Pivot D)
    {
        // 嚴格交替：bullish = LHLHL；bearish = HLHLH
        string direction;
        if (!X.IsHigh && A.IsHigh && !B.IsHigh && C.IsHigh && !D.IsHigh) direction = "bullish";
        else if (X.IsHigh && !A.IsHigh && B.IsHigh && !C.IsHigh && D.IsHigh) direction = "bearish";
        else return null;

        var xa = Math.Abs(A.Price - X.Price);
        var ab = Math.Abs(B.Price - A.Price);
        var bc = Math.Abs(C.Price - B.Price);
        var cd = Math.Abs(D.Price - C.Price);
        var ad = Math.Abs(D.Price - A.Price);
        if (xa <= 0 || ab <= 0 || bc <= 0) return null;

        var abXa = ab / xa;
        var bcAb = bc / ab;
        var cdBc = cd / bc;
        var adXa = ad / xa;

        PatternSpec? best = null;
        decimal bestFit = 0m;
        foreach (var p in Patterns)
        {
            var (ok, fit) = CheckPattern(abXa, bcAb, cdBc, adXa, p);
            if (ok && fit > bestFit) { best = p; bestFit = fit; }
        }
        if (best == null) return null;

        var (przLow, przHigh) = CalcPrz(direction, X.Price, A.Price, best.Ad.Min, best.Ad.Max);

        return new Detection
        {
            PatternName = best.Name,
            Direction = direction,
            Confidence = Math.Round(bestFit, 4),
            XIdx = X.Index, AIdx = A.Index, BIdx = B.Index, CIdx = C.Index, DIdx = D.Index,
            Xp = X.Price, Ap = A.Price, Bp = B.Price, Cp = C.Price, Dp = D.Price,
            AbRatio = Math.Round(abXa, 4),
            BcRatio = Math.Round(bcAb, 4),
            CdRatio = Math.Round(cdBc, 4),
            AdRatio = Math.Round(adXa, 4),
            PrzLow = przLow,
            PrzHigh = przHigh,
        };
    }

    // ── TP / SL 計算（Batch C 新增） ────────────────────────────

    /// <summary>
    /// Entry = D 的價格
    /// SL    = 過 X 點外側一點點（buffer）
    /// TP1   = CD 段 38.2% retracement（從 D 朝 C 反方向）
    /// TP2   = CD 段 61.8% retracement
    /// </summary>
    public static (decimal Entry, decimal StopLoss, decimal Tp1, decimal Tp2, decimal Rr)
        CalcTpSl(string direction, decimal Xp, decimal Cp, decimal Dp, decimal slBufferPct = 0.005m)
    {
        var entry = Dp;
        var cdSigned = Dp - Cp;
        var tp1 = Dp - cdSigned * 0.382m;
        var tp2 = Dp - cdSigned * 0.618m;

        decimal sl, risk, reward;
        if (direction == "bullish")
        {
            sl = Math.Min(Xp, Dp) * (1m - slBufferPct);
            risk = entry - sl;
            reward = tp1 - entry;
        }
        else
        {
            sl = Math.Max(Xp, Dp) * (1m + slBufferPct);
            risk = sl - entry;
            reward = entry - tp1;
        }
        var rr = risk > 0 ? Math.Round(reward / risk, 3) : 0m;
        return (
            Math.Round(entry, 4),
            Math.Round(sl, 4),
            Math.Round(tp1, 4),
            Math.Round(tp2, 4),
            rr
        );
    }

    // ── PRZ 區間計算（Batch C+ 新增、影片重點 #1） ───────────────

    /// <summary>
    /// 用 pattern 的 AD 比率範圍從 A 投影出潛在反轉區（Potential Reversal Zone）。
    ///
    /// bullish: A 是高點、PRZ 在 A 下方
    ///   PrzLow  = A - adMax × |XA|   （最深可接受 D）
    ///   PrzHigh = A - adMin × |XA|   （最淺可接受 D）
    ///   D 正常落在 PrzLow ≤ D ≤ PrzHigh
    /// bearish 對稱（PRZ 在 A 上方）。
    ///
    /// 用途：
    ///   1. 偵測時驗證 D 是否真的在 PRZ（健全性檢查）
    ///   2. 偵測後判斷 invalidation：價突破 PrzLow（bullish） / PrzHigh（bearish）視為失效
    /// </summary>
    public static (decimal Low, decimal High) CalcPrz(
        string direction, decimal Xp, decimal Ap, decimal adMin, decimal adMax)
    {
        var xaLen = Math.Abs(Ap - Xp);
        if (direction == "bullish")
        {
            return (
                Math.Round(Ap - adMax * xaLen, 4),
                Math.Round(Ap - adMin * xaLen, 4)
            );
        }
        else
        {
            return (
                Math.Round(Ap + adMin * xaLen, 4),
                Math.Round(Ap + adMax * xaLen, 4)
            );
        }
    }

    // ── Candle Confirmation 掃描（Batch C+ 新增、影片重點 #2） ────

    /// <summary>
    /// 從 D 後 [1, window] 根 K 線中、找與形態方向一致的反轉燭線確認訊號。
    /// bullish：Hammer / Bullish_Engulfing
    /// bearish：Shooting_Star / Bearish_Engulfing
    /// 用既有 PriceActionPatterns 偵測器、保持模組對稱。
    ///
    /// 註：RSI divergence 留下批；目前只做燭線層。
    /// </summary>
    public static (bool HasConfirm, string Signals) DetectCandleConfirmation(
        List<BarData> bars, int dIndex, string direction, int window = 5)
    {
        var maxIdx = Math.Min(bars.Count - 1, dIndex + window);
        if (maxIdx <= dIndex) return (false, "");

        var pins    = PriceActionPatterns.DetectPinBar(bars);
        var engulfs = PriceActionPatterns.DetectEngulfing(bars);
        var signals = new List<string>();

        foreach (var p in pins)
        {
            if (p.BarIndex <= dIndex || p.BarIndex > maxIdx) continue;
            if (direction == "bullish"  && p.Type == "Hammer")        signals.Add($"Hammer@{p.BarIndex}");
            if (direction == "bearish" && p.Type == "Shooting_Star")  signals.Add($"ShootingStar@{p.BarIndex}");
        }
        foreach (var e in engulfs)
        {
            if (e.BarIndex <= dIndex || e.BarIndex > maxIdx) continue;
            if (direction == "bullish" && e.Type == "Bullish_Engulfing") signals.Add($"BullEngulf@{e.BarIndex}");
            if (direction == "bearish" && e.Type == "Bearish_Engulfing") signals.Add($"BearEngulf@{e.BarIndex}");
        }
        return (signals.Count > 0, string.Join(",", signals));
    }

    // ── Status 評估（Batch C 新增、Batch C+ 加 Invalidated/PRZ） ─

    /// <summary>
    /// 從 D 那根 K 線之後到當下、看 high/low 哪個先觸發、回傳 status 跟距 D 過了幾根。
    ///
    /// 優先順序（先到的為準）：
    ///   1. SL hit （硬性停損、超過 X 一段、最壞情況）
    ///   2. TP2 hit
    ///   3. TP1 hit
    ///   4. Invalidated （價突破 PRZ 區間但未到 SL、影片重點 #3 軟失效）
    ///
    /// PRZ 參數選填、不傳就只做 SL/TP 判定（向後相容）。
    /// </summary>
    public static (PatternStatus Status, int BarsSinceD) EvaluateStatus(
        string direction, List<BarData> bars, int dIndex,
        decimal entry, decimal sl, decimal tp1, decimal tp2,
        decimal? przLow = null, decimal? przHigh = null)
    {
        var n = bars.Count;
        var barsSince = Math.Max(0, n - 1 - dIndex);
        if (dIndex >= n - 1) return (PatternStatus.Open, 0);

        for (int k = dIndex + 1; k < n; k++)
        {
            var b = bars[k];
            if (direction == "bullish")
            {
                if (b.Low  <= sl)                                       return (PatternStatus.SlHit,       k - dIndex);
                if (b.High >= tp2)                                      return (PatternStatus.Tp2Hit,      k - dIndex);
                if (b.High >= tp1)                                      return (PatternStatus.Tp1Hit,      k - dIndex);
                if (przLow.HasValue && b.Low <= przLow.Value)           return (PatternStatus.Invalidated, k - dIndex);
            }
            else
            {
                if (b.High >= sl)                                       return (PatternStatus.SlHit,       k - dIndex);
                if (b.Low  <= tp2)                                      return (PatternStatus.Tp2Hit,      k - dIndex);
                if (b.Low  <= tp1)                                      return (PatternStatus.Tp1Hit,      k - dIndex);
                if (przHigh.HasValue && b.High >= przHigh.Value)        return (PatternStatus.Invalidated, k - dIndex);
            }
        }
        return (PatternStatus.Open, barsSince);
    }

    // ── DetectAll（Batch C 新增） ───────────────────────────────

    /// <summary>
    /// 在 bars 中偵測所有諧波形態。每個 D pivot 點只回最佳匹配（一個 D 不會有多形態同時）。
    /// 依 D 由新到舊排序。每個 Detection 含 TP/SL/Status/BarsSinceD 完整資訊。
    /// </summary>
    public static List<Detection> DetectAll(
        List<BarData> bars,
        int pivotWindow = 3,
        int minBarsXa = 3,
        int maxAgeBars = 200)
    {
        var result = new List<Detection>();
        if (bars == null || bars.Count < 30) return result;

        var pivots = FindPivots(bars, pivotWindow);
        if (pivots.Count < 5) return result;

        var n = bars.Count;
        var cutoff = n - 1 - maxAgeBars;
        var seenDIdx = new HashSet<int>();

        // 掃所有「嚴格交替」的 5 連窗
        for (int i = 0; i <= pivots.Count - 5; i++)
        {
            var X = pivots[i];
            var A = pivots[i + 1];
            var B = pivots[i + 2];
            var C = pivots[i + 3];
            var D = pivots[i + 4];

            if (X.IsHigh == A.IsHigh) continue;   // 嚴格交替過濾
            if (A.IsHigh == B.IsHigh) continue;
            if (B.IsHigh == C.IsHigh) continue;
            if (C.IsHigh == D.IsHigh) continue;

            if (D.Index < cutoff) continue;
            if (seenDIdx.Contains(D.Index)) continue;
            if (A.Index - X.Index < minBarsXa) continue;

            var det = ClassifyXabcd(X, A, B, C, D);
            if (det == null) continue;
            seenDIdx.Add(D.Index);

            // 填 TP/SL + status（含 PRZ Invalidated 判定）+ candle confirmation
            var (entry, sl, tp1, tp2, rr) = CalcTpSl(det.Direction, det.Xp, det.Cp, det.Dp);
            var (status, barsSince) = EvaluateStatus(
                det.Direction, bars, det.DIdx, entry, sl, tp1, tp2,
                przLow: det.PrzLow, przHigh: det.PrzHigh);
            var (hasConfirm, confirmSignals) = DetectCandleConfirmation(bars, det.DIdx, det.Direction);

            result.Add(new Detection
            {
                PatternName = det.PatternName,
                Direction = det.Direction,
                Confidence = det.Confidence,
                XIdx = det.XIdx, AIdx = det.AIdx, BIdx = det.BIdx, CIdx = det.CIdx, DIdx = det.DIdx,
                Xp = det.Xp, Ap = det.Ap, Bp = det.Bp, Cp = det.Cp, Dp = det.Dp,
                AbRatio = det.AbRatio, BcRatio = det.BcRatio, CdRatio = det.CdRatio, AdRatio = det.AdRatio,
                Entry = entry, StopLoss = sl, Tp1 = tp1, Tp2 = tp2, RiskReward = rr,
                Status = status, BarsSinceD = barsSince,
                PrzLow = det.PrzLow, PrzHigh = det.PrzHigh,
                HasCandleConfirmation = hasConfirm, ConfirmationSignals = confirmSignals,
            });
        }

        return result.OrderByDescending(d => d.DIdx).ToList();
    }

    // ── 既有 Detect（backward compat）: 回最新一個、無則 "none" ──

    public static Detection Detect(List<BarData> bars, int pivotWindow = 3)
    {
        var all = DetectAll(bars, pivotWindow, minBarsXa: 1, maxAgeBars: int.MaxValue);
        if (all.Count == 0)
            return new Detection { PatternName = "none", Direction = "none" };
        return all[0];
    }

    // ── SimulatePath（Batch C 新增） ────────────────────────────

    /// <summary>
    /// 拿歷史同類型的已完成形態當參考、投影 target 的未來 N 根 K 線平均路徑 + 命中率統計。
    ///
    /// 用途：給 dashboard / LLM 一個「同型態歷史上怎麼走、TP/SL 觸發率多少」的依據、
    /// 取代「pattern fit_score=0.85」這種抽象數值。
    /// </summary>
    public static SimulationResult SimulatePath(
        List<BarData> bars,
        Detection target,
        int projectionBars = 20,
        int maxExamples = 20)
    {
        if (target == null || string.IsNullOrEmpty(target.PatternName) || target.PatternName == "none")
            return new SimulationResult { Note = "target invalid" };

        // 撈歷史同類型形態（不限年齡）
        var historical = DetectAll(bars, maxAgeBars: int.MaxValue);
        var nTotal = bars.Count;
        var examples = new List<Detection>();
        foreach (var p in historical)
        {
            if (p.PatternName != target.PatternName) continue;
            if (p.Direction   != target.Direction)   continue;
            if (p.DIdx == target.DIdx)               continue;   // 跳自己
            if (nTotal - 1 - p.DIdx < projectionBars) continue;  // 後續 K 線不夠
            examples.Add(p);
            if (examples.Count >= maxExamples) break;
        }

        if (examples.Count == 0)
        {
            return new SimulationResult
            {
                SampleCount = 0,
                Note = $"無歷史 {target.Direction}_{target.PatternName} 樣本可參考",
            };
        }

        // 每根 K 線蒐集所有樣本的 close/d_close ratio
        var ratiosPerBar = new List<List<decimal>>();
        for (int k = 0; k <= projectionBars; k++) ratiosPerBar.Add(new List<decimal>());

        var tp1HitBars = new List<int>();
        var tp2HitBars = new List<int>();
        var slHitBars  = new List<int>();

        if (target.Entry == 0m) target = WithComputedTargets(target);
        var targetEntry = target.Entry;
        var targetTp1Ratio = targetEntry == 0m ? 0m : target.Tp1 / targetEntry;
        var targetTp2Ratio = targetEntry == 0m ? 0m : target.Tp2 / targetEntry;
        var targetSlRatio  = targetEntry == 0m ? 0m : target.StopLoss / targetEntry;

        foreach (var p in examples)
        {
            var dClose = bars[p.DIdx].Close;
            if (dClose <= 0m) continue;

            int? hitTp1 = null, hitTp2 = null, hitSl = null;
            for (int k = 0; k <= projectionBars; k++)
            {
                var idx = p.DIdx + k;
                if (idx >= nTotal) break;
                var ratio = bars[idx].Close / dClose;
                ratiosPerBar[k].Add(ratio);

                if (target.Direction == "bullish")
                {
                    if (hitTp1 == null && ratio >= targetTp1Ratio) hitTp1 = k;
                    if (hitTp2 == null && ratio >= targetTp2Ratio) hitTp2 = k;
                    if (hitSl  == null && ratio <= targetSlRatio)  hitSl  = k;
                }
                else
                {
                    if (hitTp1 == null && ratio <= targetTp1Ratio) hitTp1 = k;
                    if (hitTp2 == null && ratio <= targetTp2Ratio) hitTp2 = k;
                    if (hitSl  == null && ratio >= targetSlRatio)  hitSl  = k;
                }
            }
            if (hitTp1 is > 0) tp1HitBars.Add(hitTp1.Value);
            if (hitTp2 is > 0) tp2HitBars.Add(hitTp2.Value);
            if (hitSl  is > 0) slHitBars.Add(hitSl.Value);
        }

        var avgPath = new List<PathPoint>();
        for (int k = 0; k <= projectionBars; k++)
        {
            var samples = ratiosPerBar[k];
            if (samples.Count == 0) continue;
            decimal sum = 0m;
            foreach (var r in samples) sum += r;
            var avgRatio = sum / samples.Count;
            avgPath.Add(new PathPoint
            {
                Bar = k,
                Ratio = Math.Round(avgRatio, 6),
                Price = Math.Round(targetEntry * avgRatio, 4),
                Samples = samples.Count,
            });
        }

        var nExamples = examples.Count;
        var stats = new SimulationStats
        {
            Samples = nExamples,
            Tp1HitPct = nExamples == 0 ? 0m : Math.Round(100m * tp1HitBars.Count / nExamples, 1),
            Tp2HitPct = nExamples == 0 ? 0m : Math.Round(100m * tp2HitBars.Count / nExamples, 1),
            SlHitPct  = nExamples == 0 ? 0m : Math.Round(100m * slHitBars.Count  / nExamples, 1),
            AvgBarsToTp1 = tp1HitBars.Count == 0 ? null : Math.Round((decimal)tp1HitBars.Sum() / tp1HitBars.Count, 1),
            AvgBarsToTp2 = tp2HitBars.Count == 0 ? null : Math.Round((decimal)tp2HitBars.Sum() / tp2HitBars.Count, 1),
            AvgBarsToSl  = slHitBars.Count  == 0 ? null : Math.Round((decimal)slHitBars.Sum()  / slHitBars.Count,  1),
        };

        return new SimulationResult
        {
            SampleCount = nExamples,
            AvgPath = avgPath,
            Stats = stats,
        };
    }

    /// <summary>給沒填 Entry/Tp/Sl 的 Detection 補上（向後相容用）。</summary>
    private static Detection WithComputedTargets(Detection d)
    {
        var (entry, sl, tp1, tp2, rr) = CalcTpSl(d.Direction, d.Xp, d.Cp, d.Dp);
        return new Detection
        {
            PatternName = d.PatternName, Direction = d.Direction, Confidence = d.Confidence,
            XIdx = d.XIdx, AIdx = d.AIdx, BIdx = d.BIdx, CIdx = d.CIdx, DIdx = d.DIdx,
            Xp = d.Xp, Ap = d.Ap, Bp = d.Bp, Cp = d.Cp, Dp = d.Dp,
            AbRatio = d.AbRatio, BcRatio = d.BcRatio, CdRatio = d.CdRatio, AdRatio = d.AdRatio,
            Entry = entry, StopLoss = sl, Tp1 = tp1, Tp2 = tp2, RiskReward = rr,
            Status = d.Status, BarsSinceD = d.BarsSinceD,
            PrzLow = d.PrzLow, PrzHigh = d.PrzHigh,
            HasCandleConfirmation = d.HasCandleConfirmation, ConfirmationSignals = d.ConfirmationSignals,
        };
    }
}
