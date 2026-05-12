using StrategyWorker.Models;

namespace StrategyWorker.Engine.Indicators;

/// <summary>
/// MFI（Money Flow Index）— 成交量加權的 RSI、又稱「資金 RSI」。
///
/// 算法（period=14）：
///   TP[i]    = (H + L + C) / 3
///   MoneyFlow[i] = TP[i] × Volume[i]
///   PosMF = sum over period where TP[i] &gt; TP[i-1]
///   NegMF = sum over period where TP[i] &lt; TP[i-1]
///   MFI   = 100 - 100 / (1 + PosMF / NegMF)
///
/// 解讀：
///   MFI &lt; 20 → 資金面超賣（買訊）
///   MFI &gt; 80 → 資金面超買（賣訊）
///   ±50 區間中性
///
/// 跟 RSI 差別：MFI 多了 Volume 加權、Volume 大的方向會被放大、更貼近實際資金流。
///
/// 設計參考：朋友 ai-quant-starter2/app/services/strategy_selector.py:s_mfi。
/// </summary>
public static class Mfi
{
    public static decimal? Compute(List<BarData> bars, int period = 14)
    {
        // 需要 period+1 根：第一根算不出 TP 差、第二根才開始有 pos/neg MF
        if (bars == null || bars.Count < period + 1) return null;

        var lastIdx = bars.Count - 1;
        decimal posMf = 0m, negMf = 0m;
        decimal prevTp = (bars[lastIdx - period].High + bars[lastIdx - period].Low + bars[lastIdx - period].Close) / 3m;

        for (int i = lastIdx - period + 1; i <= lastIdx; i++)
        {
            var b = bars[i];
            var tp = (b.High + b.Low + b.Close) / 3m;
            var vol = b.Volume == 0m ? 1m : b.Volume;
            var mf = tp * vol;
            if (tp > prevTp)      posMf += mf;
            else if (tp < prevTp) negMf += mf;
            prevTp = tp;
        }

        if (negMf == 0m) return 100m;
        var mfi = 100m - 100m / (1m + posMf / negMf);
        return Math.Round(mfi, 4);
    }
}
