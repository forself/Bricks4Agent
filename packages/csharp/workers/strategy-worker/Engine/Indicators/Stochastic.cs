using StrategyWorker.Models;

namespace StrategyWorker.Engine.Indicators;

/// <summary>
/// Stochastic Oscillator（隨機指標 KD）。
///
/// 公式：
///   %K[i] = 100 × (Close[i] - min(L, kPeriod)) / (max(H, kPeriod) - min(L, kPeriod))
///   %D[i] = SMA(%K, dPeriod)
///
/// 常見參數：kPeriod=14, dPeriod=3（標準）。短線交易者也會用 9/3 或 5/3。
///
/// 解讀：
///   %K < 20  → 超賣
///   %K > 80  → 超買
///   %K 上穿 %D → 黃金交叉（短線買訊）
///   %K 下穿 %D → 死亡交叉（短線賣訊）
///
/// 跟 RSI 的差異：
///   - RSI 算「漲跌幅變化」、Stoch 算「位置在區間中的相對高低」
///   - Stoch 對橫盤更敏感、RSI 對趨勢中強弱更敏感
///   - 兩者**雙重共振**（同時超買/超賣）是經典強訊號（friend rsi_stoch strategy）
///
/// 設計參考：朋友 ai-quant-starter2/app/services/strategy_engine.py:_calc_indicators
/// + strategy_selector.py:s_rsi_stoch。
/// </summary>
public static class Stochastic
{
    public class Result
    {
        public decimal K { get; init; }   // %K
        public decimal D { get; init; }   // %D
    }

    public static Result? Compute(List<BarData> bars, int kPeriod = 14, int dPeriod = 3)
    {
        if (bars == null || bars.Count < kPeriod + dPeriod - 1) return null;

        var n = bars.Count;

        // 算所有有效 i 的 %K
        var kArr = new decimal[n];
        for (int i = kPeriod - 1; i < n; i++)
        {
            decimal hi = decimal.MinValue, lo = decimal.MaxValue;
            for (int k = i - kPeriod + 1; k <= i; k++)
            {
                if (bars[k].High > hi) hi = bars[k].High;
                if (bars[k].Low  < lo) lo = bars[k].Low;
            }
            var range = hi - lo;
            kArr[i] = range == 0m ? 50m : 100m * (bars[i].Close - lo) / range;
        }

        // %D = SMA(%K, dPeriod)
        var lastIdx = n - 1;
        decimal sumD = 0m;
        for (int k = lastIdx - dPeriod + 1; k <= lastIdx; k++) sumD += kArr[k];
        var d = sumD / dPeriod;

        return new Result
        {
            K = Math.Round(kArr[lastIdx], 4),
            D = Math.Round(d, 4),
        };
    }
}
