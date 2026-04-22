using StrategyWorker.Models;

namespace StrategyWorker.Engine.Indicators;

/// <summary>
/// 布林通道（Bollinger Bands）純數學工具。
///
///   Upper = SMA(N) + k × σ(N)
///   Mid   = SMA(N)
///   Lower = SMA(N) - k × σ(N)
///
/// 其中 N 是回看期間（常用 20），k 是標準差倍數（常用 2）。
///
/// 解讀：
///   - 價格觸到 Lower → 超賣可能反轉向上
///   - 價格觸到 Upper → 超買可能回落
///   - 通道寬度收窄（squeeze）→ 波動率低，通常接著爆炸行情
///   - %b = (price - Lower) / (Upper - Lower) ∈ [0, 1] 可量化位置
/// </summary>
public static class BollingerBands
{
    public class Bands
    {
        public decimal Upper { get; init; }
        public decimal Mid   { get; init; }
        public decimal Lower { get; init; }
        public decimal BandWidth { get; init; }  // (Upper - Lower) / Mid × 100 (%)
        public decimal PercentB { get; init; }   // (price - Lower) / (Upper - Lower)
    }

    public static Bands? Compute(List<BarData> bars, decimal currentPrice, int period = 20, decimal kSigma = 2m)
    {
        if (bars.Count < period) return null;

        // Mean
        decimal sum = 0m;
        for (int i = bars.Count - period; i < bars.Count; i++) sum += bars[i].Close;
        var mean = sum / period;

        // StdDev
        decimal sqSum = 0m;
        for (int i = bars.Count - period; i < bars.Count; i++)
        {
            var d = bars[i].Close - mean;
            sqSum += d * d;
        }
        var stddev = (decimal)Math.Sqrt((double)(sqSum / period));

        var upper = mean + kSigma * stddev;
        var lower = mean - kSigma * stddev;
        var bandWidth = mean == 0 ? 0m : (upper - lower) / mean * 100m;
        var range = upper - lower;
        var percentB = range == 0 ? 0.5m : Math.Clamp((currentPrice - lower) / range, 0m, 1m);

        return new Bands
        {
            Upper = Math.Round(upper, 4),
            Mid = Math.Round(mean, 4),
            Lower = Math.Round(lower, 4),
            BandWidth = Math.Round(bandWidth, 4),
            PercentB = Math.Round(percentB, 4),
        };
    }
}
