using StrategyWorker.Models;

namespace StrategyWorker.Engine.Indicators;

/// <summary>
/// 行情類型偵測——讀近 N 根 K 線、輸出當下的 regime（趨勢/震盪/收斂/高波動）。
///
/// 給 AutoSelectStrategy 用：根據 regime 挑當下最適合的成員策略，
/// 而不是盲目跑全部策略再投票（composite/ensemble 的做法）。
///
/// 用三個指標決定 regime：
///   - SMA50 斜率：(SMA50_now - SMA50_20根前) / SMA50_20根前 × 100，正=向上趨勢
///   - ATR%：ATR(14) / close × 100，量化當下波動率
///   - BB width%：(upper - lower) / mid × 100，通道寬度（squeeze 偵測）
///
/// 用 SMA50 斜率而非標準 ADX，因為 ADX 計算冗長、且斜率對人類可解釋性更好
/// （reason 寫「slope=2.3% 趨勢向上」比 ADX=32 直觀）。
/// </summary>
public static class RegimeDetector
{
    public enum RegimeType
    {
        Unclear     = 0,  // 不夠 K 線、或介於各 regime 之間
        TrendingUp  = 1,
        TrendingDown = 2,
        RangeBound  = 3,  // 低波動橫盤
        Squeeze     = 4,  // BB 寬度極窄、即將爆破
        HighVol     = 5,  // ATR 過大（通常是大事件後）
    }

    public class Result
    {
        public RegimeType Type      { get; init; }
        public decimal Sma50Slope   { get; init; }  // %（正=向上、負=向下）
        public decimal AtrPct       { get; init; }  // %
        public decimal BbWidth      { get; init; }  // % of mid
        public bool AboveSma50      { get; init; }
        public string Description   { get; init; } = "";
    }

    public static Result Detect(List<BarData> bars)
    {
        // 至少要 50 根來算 SMA50；少於就回 Unclear
        if (bars.Count < 50)
        {
            return new Result { Type = RegimeType.Unclear, Description = "not enough bars" };
        }

        var lastClose = bars[^1].Close;

        // 1. SMA50 + 20 bars 前的 SMA50（算斜率）
        var sma50Now = AvgClose(bars, bars.Count - 50, 50);
        decimal sma50Slope = 0m;
        if (bars.Count >= 70)
        {
            var sma50Past = AvgClose(bars, bars.Count - 70, 50);
            if (sma50Past != 0m)
                sma50Slope = (sma50Now - sma50Past) / sma50Past * 100m;
        }

        // 2. ATR(14) % of price
        var atrPct = 0m;
        if (bars.Count >= 15)
        {
            var atr = ComputeAtr(bars, 14);
            if (lastClose != 0m) atrPct = atr / lastClose * 100m;
        }

        // 3. BB width
        var bb = BollingerBands.Compute(bars, lastClose, 20, 2m);
        var bbWidth = bb?.BandWidth ?? 0m;

        var aboveSma50 = lastClose > sma50Now;

        // ── Regime classification（門檻有意保守，不確定就 Unclear）─────
        RegimeType type;
        string desc;

        if (atrPct > 4m)
        {
            type = RegimeType.HighVol;
            desc = $"ATR={atrPct:F2}% > 4% 大波動";
        }
        else if (Math.Abs(sma50Slope) >= 1.0m)  // ≥ 1% over 20 bars = 強趨勢
        {
            if (sma50Slope > 0m && aboveSma50)
            {
                type = RegimeType.TrendingUp;
                desc = $"SMA50 slope=+{sma50Slope:F2}% 趨勢向上";
            }
            else if (sma50Slope < 0m && !aboveSma50)
            {
                type = RegimeType.TrendingDown;
                desc = $"SMA50 slope={sma50Slope:F2}% 趨勢向下";
            }
            else
            {
                // 斜率方向跟價格位置矛盾 → 趨勢可能轉變、不明確
                type = RegimeType.Unclear;
                desc = $"slope={sma50Slope:F2}%, price {(aboveSma50 ? "above" : "below")} SMA50 矛盾";
            }
        }
        else if (bbWidth > 0m && bbWidth < 3m)  // 通道極窄（< 3% of mid）= squeeze
        {
            type = RegimeType.Squeeze;
            desc = $"BB width={bbWidth:F2}% < 3% 收斂";
        }
        else if (Math.Abs(sma50Slope) < 0.3m)  // 幾乎平、且 BB 不窄
        {
            type = RegimeType.RangeBound;
            desc = $"slope={sma50Slope:F2}% 橫盤震盪";
        }
        else
        {
            type = RegimeType.Unclear;
            desc = $"slope={sma50Slope:F2}% 介於趨勢/震盪之間";
        }

        return new Result
        {
            Type = type,
            Sma50Slope = Math.Round(sma50Slope, 3),
            AtrPct = Math.Round(atrPct, 3),
            BbWidth = Math.Round(bbWidth, 3),
            AboveSma50 = aboveSma50,
            Description = desc,
        };
    }

    private static decimal AvgClose(List<BarData> bars, int start, int len)
    {
        if (start < 0 || start + len > bars.Count) return 0m;
        decimal sum = 0m;
        for (int i = start; i < start + len; i++) sum += bars[i].Close;
        return sum / len;
    }

    /// <summary>True Range 平均（Wilder smoothing 簡化版）。</summary>
    private static decimal ComputeAtr(List<BarData> bars, int period)
    {
        if (bars.Count < period + 1) return 0m;
        var trs = new List<decimal>(period);
        for (int i = bars.Count - period; i < bars.Count; i++)
        {
            var hi = bars[i].High;
            var lo = bars[i].Low;
            var prevClose = bars[i - 1].Close;
            var tr = Math.Max(hi - lo, Math.Max(Math.Abs(hi - prevClose), Math.Abs(lo - prevClose)));
            trs.Add(tr);
        }
        return trs.Count == 0 ? 0m : trs.Average();
    }
}
