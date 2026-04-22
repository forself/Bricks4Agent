using StrategyWorker.Models;

namespace StrategyWorker.Engine.Indicators;

/// <summary>
/// 諧波形態偵測器（Harmonic Patterns）。
///
/// 經典 5 點形態 XABCD：
///    X ──→ A ──→ B ──→ C ──→ D
///   由 Fibonacci 比率定義每段之間的比例。
///
/// 本版實作 4 種最常見形態：Gartley / Butterfly / Bat / Crab。
/// 偵測有「看多」（bullish）跟「看空」（bearish）兩個方向——
///   bullish: X 是高點、A 低、B 高、C 低、D 低（D 是進場多單的位置）
///   bearish: X 低、A 高、B 低、C 高、D 高（D 進場空單）
///
/// 實作步驟：
///   1. 用 rolling window 找 pivot high / pivot low
///   2. 取最近 5 個 pivot，依時序排成 X→A→B→C→D
///   3. 計算 |AB|/|XA|、|BC|/|AB|、|CD|/|BC|、|AD|/|XA| 四個比率
///   4. 與每個形態的比率範圍比對（有 tolerance），挑匹配最好的
///   5. 回傳形態名稱、方向、信心度、D 點價格
///
/// 參考：Scott Carney《Harmonic Trading Vol 1 &amp; 2》標準比率表。
/// </summary>
public static class HarmonicPatterns
{
    public class Pivot
    {
        public int Index { get; init; }        // bar index
        public decimal Price { get; init; }
        public bool IsHigh { get; init; }       // true = swing high，false = swing low
    }

    public class Detection
    {
        public string PatternName { get; init; } = "";    // gartley / butterfly / bat / crab / (none)
        public string Direction { get; init; } = "";       // bullish / bearish / none
        public decimal Confidence { get; init; }           // 0-1 根據比率匹配誤差
        public decimal Xp { get; init; }
        public decimal Ap { get; init; }
        public decimal Bp { get; init; }
        public decimal Cp { get; init; }
        public decimal Dp { get; init; }
        public decimal AbRatio { get; init; }   // |AB|/|XA|
        public decimal BcRatio { get; init; }   // |BC|/|AB|
        public decimal CdRatio { get; init; }   // |CD|/|BC|
        public decimal AdRatio { get; init; }   // |AD|/|XA|
    }

    // ── Pivot 偵測（簡化版：window N 內的最高/最低）──────────────────

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

        // 同 index 可能同時是 high 和 low（罕見），這時取真正的極值
        return pivots.OrderBy(p => p.Index).ToList();
    }

    // ── 四種形態的 Fibonacci 比率定義 ─────────────────────────────────

    private record PatternSpec(string Name, (decimal Min, decimal Max) Ab, (decimal Min, decimal Max) Bc, (decimal Min, decimal Max) Cd, (decimal Min, decimal Max) Ad);

    private static readonly PatternSpec[] Patterns = new[]
    {
        // 比率參考 Scott Carney 標準定義；容忍度靠 ±10% 的「匹配分數」吸收
        new PatternSpec("gartley",   (0.550m, 0.700m), (0.380m, 0.886m), (1.130m, 1.618m), (0.750m, 0.850m)),
        new PatternSpec("butterfly", (0.750m, 0.850m), (0.380m, 0.886m), (1.618m, 2.618m), (1.200m, 1.410m)),
        new PatternSpec("bat",       (0.380m, 0.500m), (0.380m, 0.886m), (1.618m, 2.618m), (0.860m, 0.920m)),
        new PatternSpec("crab",      (0.380m, 0.618m), (0.380m, 0.886m), (2.240m, 3.618m), (1.500m, 1.780m)),
    };

    // ── 主偵測流程 ────────────────────────────────────────────────────

    public static Detection Detect(List<BarData> bars, int pivotWindow = 3)
    {
        var pivots = FindPivots(bars, pivotWindow);
        if (pivots.Count < 5) return new Detection { PatternName = "none", Direction = "none" };

        // 取最近 5 個 pivot，時序 X A B C D
        var last5 = pivots.TakeLast(5).ToList();
        var X = last5[0]; var A = last5[1]; var B = last5[2]; var C = last5[3]; var D = last5[4];

        // 判斷方向：bullish XABCD → high-low-high-low-low（X 高，D 低）；bearish 反之
        // 交替模式：X 跟 B 同向、A 跟 C 同向、D 跟 A 同向（但突破程度不同）
        // 簡化判別：看 X 和 D 的 IsHigh，若 X 高/D 低 → 看多；若 X 低/D 高 → 看空
        string direction;
        if (X.IsHigh && !A.IsHigh && B.IsHigh && !C.IsHigh && !D.IsHigh) direction = "bullish";
        else if (!X.IsHigh && A.IsHigh && !B.IsHigh && C.IsHigh && D.IsHigh) direction = "bearish";
        else return new Detection { PatternName = "none", Direction = "none" };

        var xa = Math.Abs(A.Price - X.Price);
        var ab = Math.Abs(B.Price - A.Price);
        var bc = Math.Abs(C.Price - B.Price);
        var cd = Math.Abs(D.Price - C.Price);
        var ad = Math.Abs(D.Price - A.Price);

        if (xa <= 0 || ab <= 0 || bc <= 0) return new Detection { PatternName = "none", Direction = "none" };

        var abRatio = ab / xa;
        var bcRatio = bc / ab;
        var cdRatio = cd / bc;
        var adRatio = ad / xa;

        // 找匹配度最高的形態
        PatternSpec? best = null;
        decimal bestFit = 0m;
        foreach (var p in Patterns)
        {
            var fit = RatioFit(abRatio, p.Ab) * 0.30m
                    + RatioFit(bcRatio, p.Bc) * 0.20m
                    + RatioFit(cdRatio, p.Cd) * 0.25m
                    + RatioFit(adRatio, p.Ad) * 0.25m;
            if (fit > bestFit) { best = p; bestFit = fit; }
        }

        if (best == null || bestFit < 0.50m)
            return new Detection { PatternName = "none", Direction = "none" };

        return new Detection
        {
            PatternName = best.Name,
            Direction = direction,
            Confidence = Math.Round(bestFit, 4),
            Xp = X.Price, Ap = A.Price, Bp = B.Price, Cp = C.Price, Dp = D.Price,
            AbRatio = Math.Round(abRatio, 4),
            BcRatio = Math.Round(bcRatio, 4),
            CdRatio = Math.Round(cdRatio, 4),
            AdRatio = Math.Round(adRatio, 4),
        };
    }

    /// <summary>比率是否落在 [min, max] 區間；越接近中點 fit 越高，區間外遞減。</summary>
    private static decimal RatioFit(decimal actual, (decimal Min, decimal Max) range)
    {
        if (actual >= range.Min && actual <= range.Max)
        {
            var mid = (range.Min + range.Max) / 2m;
            var halfWidth = (range.Max - range.Min) / 2m;
            var dist = Math.Abs(actual - mid);
            return halfWidth == 0 ? 1m : Math.Max(0m, 1m - dist / halfWidth * 0.5m);  // in-range 至少 0.5 分
        }
        // 區間外：用 tolerance 10% 做 soft zone
        var tolerance = (range.Max - range.Min) * 0.1m;
        if (actual < range.Min)
        {
            var d = range.Min - actual;
            return Math.Max(0m, 0.3m - d / Math.Max(tolerance, 0.01m) * 0.3m);
        }
        else
        {
            var d = actual - range.Max;
            return Math.Max(0m, 0.3m - d / Math.Max(tolerance, 0.01m) * 0.3m);
        }
    }
}
