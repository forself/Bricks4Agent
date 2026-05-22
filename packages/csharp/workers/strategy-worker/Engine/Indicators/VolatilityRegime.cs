using StrategyWorker.Models;

namespace StrategyWorker.Engine.Indicators;

/// <summary>
/// 波動率狀態(ATR 百分位)—— 衡量「現在波動相對自己過去算高還低」,與價格方向正交。
///
/// 方向型指標(RSI/MACD/SMA...)回答「往哪走」,但回答不了「現在該不該出手」。
/// 波動率百分位補這個維度:
///   percentile 接近 0 → 當前 ATR 在過去 lookback 裡偏低 → 擠壓(squeeze),通常醞釀變盤。
///   percentile 接近 1 → 波動爆量 → 行情已經在跑、追進風險高。
/// 純 OHLC 算 TR → ATR(簡單移動平均)→ 在過去窗口裡排名,不需新資料源。
/// </summary>
public static class VolatilityRegime
{
    /// <summary>回傳 (當前 ATR, 百分位 0..1);資料不足回 null。</summary>
    public static (decimal Atr, decimal Percentile)? Compute(
        List<BarData> bars, int atrPeriod = 14, int lookback = 100)
    {
        if (bars == null || bars.Count < atrPeriod + lookback) return null;
        int n = bars.Count;

        // True Range 序列
        var tr = new decimal[n];
        tr[0] = bars[0].High - bars[0].Low;
        for (int i = 1; i < n; i++)
        {
            decimal h = bars[i].High, l = bars[i].Low, pc = bars[i - 1].Close;
            tr[i] = Math.Max(h - l, Math.Max(Math.Abs(h - pc), Math.Abs(l - pc)));
        }

        // ATR 序列(過去 atrPeriod 根 TR 的簡單移動平均)
        var atr = new List<decimal>();
        for (int i = atrPeriod - 1; i < n; i++)
        {
            decimal sum = 0m;
            for (int k = 0; k < atrPeriod; k++) sum += tr[i - k];
            atr.Add(sum / atrPeriod);
        }
        if (atr.Count < lookback) return null;

        // 取最後 lookback 個 ATR 當分布、算當前 ATR 的百分位(≤ current 的比例)
        var window = atr.GetRange(atr.Count - lookback, lookback);
        decimal current = window[^1];
        int leq = 0;
        foreach (var a in window) if (a <= current) leq++;
        decimal pct = (decimal)leq / window.Count;

        return (Math.Round(current, 4), Math.Round(pct, 4));
    }
}
