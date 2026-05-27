namespace StrategyWorker.Engine;

/// <summary>
/// Vol-targeting position sizer(2026-05-27 Q1.2、Roadmap Q1.2)。
///
/// 核心想法:**每期承擔的「風險」固定、不是「名目」固定**。
/// - 市場低 vol 期 → 同樣名目實際風險小、可以開大一點
/// - 市場高 vol 期 → 同樣名目實際風險大、必須開小
///
/// 跟 Kelly 互補:Kelly 給「平均該配多少」(假設長期 vol 不變)
///                Vol-target 給「現在該縮放多少」(因為現在 vol ≠ 長期平均)
///
/// 最終 size = Kelly_pct × vol_scalar
///
/// 公式:
///   realized_vol = std(daily_returns) × √365(年化)
///   vol_scalar = target_vol / realized_vol(高 vol 期 scalar < 1 → 縮)
///   clamp(vol_scalar, min=0.3, max=2.0) 防極端
///
/// 業界 default(Carver、AQR):
///   - 傳統股債:target_vol = 12-20% 年化(因 vol 平常 15%)
///   - Crypto:target_vol = 40-80% 年化(因 vol 平常 60%+ 高很多)
///   - 我們預設 60% target、約等於「BTC 長期平均 vol」
///
/// 套用例子(target_vol=60%):
///   - 牛市穩漲、realized_vol=40% → scalar = 60/40 = 1.5(放大、機會多)
///   - 崩盤期、realized_vol=120% → scalar = 60/120 = 0.5(縮一半、保命)
///   - 極端 crash、realized_vol=200% → scalar = 0.3(clamp 下限、不繼續縮)
/// </summary>
public static class VolTargetSizer
{
    /// <summary>
    /// 從 daily close 序列算年化 realized volatility(std of log returns × √365)。
    /// 至少需要 lookback+1 個 bar(算 lookback 個 returns)。
    /// </summary>
    public static decimal AnnualizedRealizedVol(List<decimal> closes, int lookback = 30)
    {
        if (closes == null || closes.Count < lookback + 1) return 0m;
        var returns = new List<double>(lookback);
        var slice = closes.TakeLast(lookback + 1).ToList();
        for (int i = 1; i < slice.Count; i++)
        {
            if (slice[i - 1] <= 0m) continue;
            var r = Math.Log((double)(slice[i] / slice[i - 1]));
            returns.Add(r);
        }
        if (returns.Count < 5) return 0m;

        double mean = returns.Average();
        double variance = returns.Sum(r => (r - mean) * (r - mean)) / (returns.Count - 1);
        double dailyStd = Math.Sqrt(variance);
        double annualizedStd = dailyStd * Math.Sqrt(365);
        return (decimal)annualizedStd;
    }

    /// <summary>
    /// 從 realized vol + target vol 算 size scalar(高 vol 期 < 1、低 vol 期 > 1)。
    /// </summary>
    public static decimal ComputeScalar(decimal realizedVol, decimal targetVol, decimal min = 0.3m, decimal max = 2.0m)
    {
        if (realizedVol <= 0m || targetVol <= 0m) return 1m;
        decimal scalar = targetVol / realizedVol;
        return Math.Clamp(scalar, min, max);
    }

    /// <summary>
    /// 一鍵 helper:從 bars + Kelly% → 最終 vol-targeted sizing。
    /// </summary>
    public static decimal FinalPct(List<decimal> closes, decimal kellyPct, decimal targetVol = 0.60m, int lookback = 30)
    {
        var realized = AnnualizedRealizedVol(closes, lookback);
        var scalar = ComputeScalar(realized, targetVol);
        return kellyPct * scalar;
    }

    /// <summary>
    /// 完整解讀(diagnostic 用、寫進 strat-validate 報告)。
    /// </summary>
    public static (decimal RealizedVol, decimal Scalar, decimal FinalPct, string Explanation) Diagnose(
        List<decimal> closes, decimal kellyPct, decimal targetVol = 0.60m, int lookback = 30)
    {
        var realized = AnnualizedRealizedVol(closes, lookback);
        var scalar = ComputeScalar(realized, targetVol);
        var final = kellyPct * scalar;

        string explanation;
        if (realized <= 0m)
            explanation = "❌ 資料不足、用 scalar=1(無 vol-target 調整)";
        else if (scalar >= 1.5m)
            explanation = $"📈 低 vol regime(realized {realized:P0} << target {targetVol:P0})、放大 {scalar:F2}×";
        else if (scalar <= 0.7m)
            explanation = $"📉 高 vol regime(realized {realized:P0} >> target {targetVol:P0})、縮 {scalar:F2}×";
        else
            explanation = $"🟢 vol 接近 target(realized {realized:P0} vs {targetVol:P0})、scalar {scalar:F2}";

        return (realized, scalar, final, explanation);
    }
}
