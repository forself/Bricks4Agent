using StrategyWorker.Models;

namespace StrategyWorker.Engine.Indicators;

/// <summary>
/// Parabolic SAR（Stop And Reverse）— J. Welles Wilder 的反轉停損點。
///
/// 演算法（與朋友 repo 對齊）：
///   1. 初始：bullish=true、SAR=第一根 low、極端點 EP=第一根 high、加速因子 AF=0.02
///   2. 每根 K：
///      - bullish 模式：
///          若 low &lt; SAR → 反轉成 bearish、SAR=EP、EP=low、AF=0.02
///          否則：高過 EP 就更新 EP=high 並 AF=min(AF+0.02, 0.2)
///                  SAR = SAR + AF × (EP - SAR)、然後 clip 到前兩根 low 的下方
///      - bearish 模式對稱
///   3. 回最後一根的 SAR + 方向旗 + 距 price 的百分比
///
/// path-dependent（從頭算到底）但只用過去資料、truncation invariant 成立。
///
/// 設計參考：朋友 ai-quant-starter2/app/services/strategy_selector.py:s_parabolic_sar。
/// </summary>
public static class ParabolicSar
{
    public class Result
    {
        public decimal Sar         { get; init; }
        public bool    IsBullish   { get; init; }
        public decimal DistancePct { get; init; }   // |price - sar| / price × 100
    }

    public static Result? Compute(
        List<BarData> bars,
        decimal initialAf = 0.02m,
        decimal afStep    = 0.02m,
        decimal maxAf     = 0.2m)
    {
        if (bars == null || bars.Count < 3) return null;

        var n = bars.Count;
        decimal sar    = bars[0].Low;
        decimal ep     = bars[0].High;
        decimal af     = initialAf;
        bool    bull   = true;

        for (int i = 0; i < n; i++)
        {
            var hv = bars[i].High;
            var lv = bars[i].Low;
            if (bull)
            {
                if (lv < sar)
                {
                    bull = false;
                    sar  = ep;
                    ep   = lv;
                    af   = initialAf;
                }
                else
                {
                    if (hv > ep)
                    {
                        ep = hv;
                        af = Math.Min(af + afStep, maxAf);
                    }
                    sar = sar + af * (ep - sar);
                    // SAR 不可大於前兩根 low（避免在 bull 模式 SAR 跨入當前 K 範圍）
                    var prev1Low = bars[Math.Max(0, i - 1)].Low;
                    var prev2Low = bars[Math.Max(0, i - 2)].Low;
                    sar = Math.Min(sar, Math.Min(prev1Low, prev2Low));
                }
            }
            else
            {
                if (hv > sar)
                {
                    bull = true;
                    sar  = ep;
                    ep   = hv;
                    af   = initialAf;
                }
                else
                {
                    if (lv < ep)
                    {
                        ep = lv;
                        af = Math.Min(af + afStep, maxAf);
                    }
                    sar = sar + af * (ep - sar);
                    var prev1High = bars[Math.Max(0, i - 1)].High;
                    var prev2High = bars[Math.Max(0, i - 2)].High;
                    sar = Math.Max(sar, Math.Max(prev1High, prev2High));
                }
            }
        }

        var price = bars[n - 1].Close;
        var dist  = price == 0m ? 0m : Math.Abs(price - sar) / price * 100m;
        return new Result
        {
            Sar         = Math.Round(sar, 6),
            IsBullish   = bull,
            DistancePct = Math.Round(dist, 4),
        };
    }
}
