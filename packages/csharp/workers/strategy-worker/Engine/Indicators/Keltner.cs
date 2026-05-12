using StrategyWorker.Models;

namespace StrategyWorker.Engine.Indicators;

/// <summary>
/// Keltner Channel — EMA 中軌 + ATR 上下軌、布林帶的 ATR 版本。
///
///   Mid   = EMA(Close, emaPeriod)
///   ATR   = SMA(TR, atrPeriod)        ← 簡化版（朋友 repo 用 SMA、非 Wilder）
///   Upper = Mid + multiplier × ATR
///   Lower = Mid - multiplier × ATR
///
/// 預設 emaPeriod=20, atrPeriod=10, multiplier=2。
///
/// 解讀（mean-reversion）：
///   收盤 &gt; Upper → 超買（朋友 repo 判 -score）
///   收盤 &lt; Lower → 超賣（+score）
///   收盤 在通道內 → 跟中軌方向同向小信心
///
/// 設計參考：朋友 ai-quant-starter2/app/services/strategy_selector.py:s_keltner。
/// </summary>
public static class Keltner
{
    public class Result
    {
        public decimal Upper       { get; init; }
        public decimal Mid         { get; init; }
        public decimal Lower       { get; init; }
        public decimal ChannelWidthPct { get; init; }
    }

    public static Result? Compute(
        List<BarData> bars,
        int emaPeriod = 20,
        int atrPeriod = 10,
        decimal multiplier = 2m)
    {
        var maxNeeded = Math.Max(emaPeriod, atrPeriod) + 1;
        if (bars == null || bars.Count < maxNeeded) return null;

        var n = bars.Count;

        // EMA(close) 從頭累計、用 span 公式 α = 2/(N+1)
        var alpha = 2m / (emaPeriod + 1);
        decimal ema = bars[0].Close;
        for (int i = 1; i < n; i++)
            ema = alpha * bars[i].Close + (1m - alpha) * ema;

        // TR + SMA(ATR)
        var lastIdx = n - 1;
        decimal trSum = 0m;
        for (int i = lastIdx - atrPeriod + 1; i <= lastIdx; i++)
        {
            var b = bars[i];
            var prevClose = bars[i - 1].Close;
            var tr = Math.Max(b.High - b.Low,
                              Math.Max(Math.Abs(b.High - prevClose), Math.Abs(b.Low - prevClose)));
            trSum += tr;
        }
        var atr = trSum / atrPeriod;

        var upper = ema + multiplier * atr;
        var lower = ema - multiplier * atr;
        var width = ema == 0m ? 0m : (upper - lower) / ema * 100m;

        return new Result
        {
            Upper = Math.Round(upper, 6),
            Mid   = Math.Round(ema, 6),
            Lower = Math.Round(lower, 6),
            ChannelWidthPct = Math.Round(width, 4),
        };
    }
}
