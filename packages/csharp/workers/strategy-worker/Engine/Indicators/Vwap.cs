using StrategyWorker.Models;

namespace StrategyWorker.Engine.Indicators;

/// <summary>
/// VWAP（Volume Weighted Average Price）— 量價加權平均。
///
/// 公式（rolling window 版本、朋友 repo 採用）：
///   TP[i]   = (H[i] + L[i] + C[i]) / 3                   ← Typical Price
///   VWAP[i] = Σ(TP[k] × Vol[k], k = i-N+1..i) / Σ(Vol[k])
///
/// 註：傳統 VWAP 是「從交易日開盤累計到當下」、本實作對齊朋友 repo 用 **rolling-20**、
/// 因為加密貨幣市場 24/7 沒有「交易日重置」概念、rolling 版本更實用。
///
/// 解讀：
///   收盤 > VWAP → 價格高於近期成本均、機構偏多
///   收盤 < VWAP → 機構偏空
///   |dist| > 2% → 嚴重偏離、可能均值回歸
///
/// 沒 Volume 欄位（罕見）→ 退化成單純的 TP 移動平均。
///
/// 設計參考：朋友 ai-quant-starter2/app/services/strategy_selector.py:s_vwap。
/// </summary>
public static class Vwap
{
    public class Result
    {
        public decimal Value      { get; init; }   // VWAP 當前值
        public decimal DeviationPct{ get; init; }  // (price - vwap) / vwap × 100
    }

    public static Result? Compute(List<BarData> bars, int period = 20)
    {
        if (bars == null || bars.Count < period) return null;

        var lastIdx = bars.Count - 1;
        decimal sumTpVol = 0m;
        decimal sumVol   = 0m;
        for (int i = lastIdx - period + 1; i <= lastIdx; i++)
        {
            var tp = (bars[i].High + bars[i].Low + bars[i].Close) / 3m;
            // Volume = 0 的 bar 還是有 TP 貢獻（避免完全跳過、退化成 TP 平均）
            var vol = bars[i].Volume == 0m ? 1m : bars[i].Volume;
            sumTpVol += tp * vol;
            sumVol   += vol;
        }
        if (sumVol == 0m) return null;
        var vwap = sumTpVol / sumVol;

        var price = bars[lastIdx].Close;
        var dev   = vwap == 0m ? 0m : (price - vwap) / vwap * 100m;

        return new Result
        {
            Value        = Math.Round(vwap, 6),
            DeviationPct = Math.Round(dev, 4),
        };
    }
}
