using StrategyWorker.Models;

namespace StrategyWorker.Engine.Indicators;

/// <summary>
/// 一目均衡表（Ichimoku Kinko Hyo）——日本五線雲圖系統的簡化版。
///
/// 公式：
///   Tenkan-sen (轉換線、9):   (max(H,9)  + min(L,9))  / 2
///   Kijun-sen  (基準線、26):  (max(H,26) + min(L,26)) / 2
///   Senkou Span A (先行帶 A): (Tenkan + Kijun) / 2
///   Senkou Span B (先行帶 B): (max(H,52) + min(L,52)) / 2
///
/// 註：完整版的 Senkou Span A/B 要往未來位移 26 根、Chikou Span 是收盤往後位移 26。
/// 本實作對齊朋友 repo 的**簡化「當下值」版本**——對齊用、no-lookahead 友善、
/// 沒有未來偏移、可被 strategy 即時引用。
///
/// 解讀：
///   收盤 > max(ssa, ssb)（雲層之上） → 多頭結構
///   收盤 < min(ssa, ssb)（雲層之下） → 空頭結構
///   收盤 在雲層內部                    → 震盪 / 不確定
///   Tenkan > Kijun                      → 短線偏多（金叉）
///   Tenkan < Kijun                      → 短線偏空（死叉）
///
/// 設計參考：朋友 ai-quant-starter2/app/services/strategy_selector.py:s_ichimoku。
/// </summary>
public static class Ichimoku
{
    public class Result
    {
        public decimal Tenkan     { get; init; }
        public decimal Kijun      { get; init; }
        public decimal SenkouSpanA { get; init; }
        public decimal SenkouSpanB { get; init; }
        public decimal CloudTop    { get; init; }
        public decimal CloudBottom { get; init; }
        public string  PricePosition { get; init; } = "in_cloud";   // above_cloud / in_cloud / below_cloud
        public string  TkCross       { get; init; } = "flat";       // bullish / bearish / flat
    }

    public static Result? Compute(
        List<BarData> bars,
        int tenkanPeriod = 9,
        int kijunPeriod = 26,
        int ssbPeriod = 52)
    {
        if (bars == null || bars.Count < ssbPeriod) return null;

        var lastIdx = bars.Count - 1;
        var price   = bars[lastIdx].Close;

        var tenkan = MidOfRange(bars, lastIdx, tenkanPeriod);
        var kijun  = MidOfRange(bars, lastIdx, kijunPeriod);
        var ssa    = (tenkan + kijun) / 2m;
        var ssb    = MidOfRange(bars, lastIdx, ssbPeriod);

        var cloudTop = Math.Max(ssa, ssb);
        var cloudBot = Math.Min(ssa, ssb);

        string pos =
            price > cloudTop ? "above_cloud" :
            price < cloudBot ? "below_cloud" : "in_cloud";

        string cross =
            tenkan > kijun ? "bullish" :
            tenkan < kijun ? "bearish" : "flat";

        return new Result
        {
            Tenkan        = Math.Round(tenkan, 6),
            Kijun         = Math.Round(kijun, 6),
            SenkouSpanA   = Math.Round(ssa, 6),
            SenkouSpanB   = Math.Round(ssb, 6),
            CloudTop      = Math.Round(cloudTop, 6),
            CloudBottom   = Math.Round(cloudBot, 6),
            PricePosition = pos,
            TkCross       = cross,
        };
    }

    private static decimal MidOfRange(List<BarData> bars, int endIdx, int period)
    {
        decimal hi = decimal.MinValue, lo = decimal.MaxValue;
        for (int i = endIdx - period + 1; i <= endIdx; i++)
        {
            if (bars[i].High > hi) hi = bars[i].High;
            if (bars[i].Low  < lo) lo = bars[i].Low;
        }
        return (hi + lo) / 2m;
    }
}
