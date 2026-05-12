using StrategyWorker.Models;

namespace StrategyWorker.Engine.Indicators;

/// <summary>
/// CCI（Commodity Channel Index）— Donald Lambert 的商品通道指數。
///
///   TP[i]    = (H[i] + L[i] + C[i]) / 3      ← typical price
///   TpMA     = SMA(TP, period)
///   MAD      = mean abs deviation of TP over period
///   CCI[i]   = (TP[i] - TpMA[i]) / (0.015 × MAD[i])
///
/// 解讀：
///   CCI &gt; +100 → 超買、可能回落
///   CCI &lt; -100 → 超賣、可能反彈
///   CCI 在 ±50 區間視為「中性」
///
/// 預設 period=20、constant 0.015 是 Lambert 原版設計（讓 ~70-80% 的值落在 ±100 之內）。
///
/// 設計參考：朋友 ai-quant-starter2/app/services/strategy_selector.py:s_cci。
/// </summary>
public static class Cci
{
    public static decimal? Compute(List<BarData> bars, int period = 20)
    {
        if (bars == null || bars.Count < period) return null;

        var lastIdx = bars.Count - 1;
        // 算最後 period 根的 TP 跟平均
        decimal tpSum = 0m;
        var tps = new decimal[period];
        for (int k = 0; k < period; k++)
        {
            var b = bars[lastIdx - period + 1 + k];
            tps[k] = (b.High + b.Low + b.Close) / 3m;
            tpSum += tps[k];
        }
        var tpMa = tpSum / period;

        // mean abs deviation
        decimal madSum = 0m;
        for (int k = 0; k < period; k++) madSum += Math.Abs(tps[k] - tpMa);
        var mad = madSum / period;

        if (mad == 0m) return 0m;
        var lastTp = tps[period - 1];
        var cci = (lastTp - tpMa) / (0.015m * mad);
        return Math.Round(cci, 4);
    }
}
