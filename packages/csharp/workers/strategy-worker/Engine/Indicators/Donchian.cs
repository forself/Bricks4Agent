using StrategyWorker.Models;

namespace StrategyWorker.Engine.Indicators;

/// <summary>
/// Donchian Channel — 經典 Turtle 系統的突破通道。
///
///   Upper = max(High, N)   ← N 根高點
///   Lower = min(Low,  N)
///   Mid   = (Upper + Lower) / 2
///   ChannelWidth = (Upper - Lower) / Mid （%）
///
/// 突破訊號：
///   收盤 ≥ 「前一根」的 N 根高點 → 多頭突破
///   收盤 ≤ 「前一根」的 N 根低點 → 空頭突破
/// 「前一根」是關鍵——避免當前 K 棒自己塞進 rolling max 自體比較。
///
/// 通道寬度 &lt; 5% Mid → 收窄（squeeze、突破前蓄能）。
///
/// 設計參考：朋友 ai-quant-starter2/app/services/strategy_selector.py:s_donchian。
/// </summary>
public static class Donchian
{
    public class Result
    {
        public decimal Upper        { get; init; }
        public decimal Lower        { get; init; }
        public decimal Mid          { get; init; }
        public decimal ChannelWidthPct { get; init; }   // %
        public decimal PrevUpper    { get; init; }      // 前一根（不含當前）的 N 根高點
        public decimal PrevLower    { get; init; }      // 前一根的 N 根低點
    }

    public static Result? Compute(List<BarData> bars, int period = 20)
    {
        if (bars == null || bars.Count < period + 1) return null;

        var lastIdx = bars.Count - 1;
        decimal upper = decimal.MinValue, lower = decimal.MaxValue;
        for (int i = lastIdx - period + 1; i <= lastIdx; i++)
        {
            if (bars[i].High > upper) upper = bars[i].High;
            if (bars[i].Low  < lower) lower = bars[i].Low;
        }
        var mid = (upper + lower) / 2m;
        var width = mid == 0m ? 0m : (upper - lower) / mid * 100m;

        // 前一根的 N 根區間（不含當前 K）= bars[lastIdx-period .. lastIdx-1]
        decimal prevUpper = decimal.MinValue, prevLower = decimal.MaxValue;
        for (int i = lastIdx - period; i <= lastIdx - 1; i++)
        {
            if (bars[i].High > prevUpper) prevUpper = bars[i].High;
            if (bars[i].Low  < prevLower) prevLower = bars[i].Low;
        }

        return new Result
        {
            Upper           = Math.Round(upper, 6),
            Lower           = Math.Round(lower, 6),
            Mid             = Math.Round(mid, 6),
            ChannelWidthPct = Math.Round(width, 4),
            PrevUpper       = Math.Round(prevUpper, 6),
            PrevLower       = Math.Round(prevLower, 6),
        };
    }
}
