using StrategyWorker.Models;

namespace StrategyWorker.Engine.Indicators;

/// <summary>
/// Chaikin Money Flow (CMF) — Marc Chaikin 的資金流量。
///
/// 算法（period=20）：
///   CLV[i] = ((C - L) - (H - C)) / (H - L)        ← Close Location Value ∈ [-1, +1]
///   MFV[i] = CLV[i] × Volume[i]                    ← Money Flow Volume
///   CMF    = sum(MFV, period) / sum(Volume, period)
///
/// 解讀：
///   CMF &gt; +0.1 → 強力買盤（價多收靠近高點 + 大量）
///   CMF &gt; 0    → 溫和買盤
///   CMF &lt; -0.1 → 強力賣盤
///   CMF 在 ±0.05 內視為中性
///
/// 跟 OBV 的差別：CMF 用 CLV 加權（K 棒位置）、OBV 只看 close 漲跌方向。CMF 對「上影線/下影線」更敏感。
///
/// 設計參考：朋友 ai-quant-starter2/app/services/strategy_selector.py:s_chaikin_mf。
/// </summary>
public static class ChaikinMf
{
    public static decimal? Compute(List<BarData> bars, int period = 20)
    {
        if (bars == null || bars.Count < period) return null;

        var lastIdx = bars.Count - 1;
        decimal mfvSum = 0m, volSum = 0m;

        for (int i = lastIdx - period + 1; i <= lastIdx; i++)
        {
            var b = bars[i];
            var range = b.High - b.Low;
            if (range == 0m) continue;   // 沒振幅、CLV 未定義、跳過該根（朋友 repo 同樣 replace(0, nan) 後忽略）
            var clv = ((b.Close - b.Low) - (b.High - b.Close)) / range;
            var vol = b.Volume == 0m ? 1m : b.Volume;
            mfvSum += clv * vol;
            volSum += vol;
        }
        if (volSum == 0m) return 0m;
        var cmf = mfvSum / volSum;
        return Math.Round(cmf, 6);
    }
}
