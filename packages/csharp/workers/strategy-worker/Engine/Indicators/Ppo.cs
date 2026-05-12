namespace StrategyWorker.Engine.Indicators;

/// <summary>
/// PPO (Percentage Price Oscillator) — MACD 的百分比正規化版：
///   PPO = (EMA(short) - EMA(long)) / EMA(long) × 100
///   Signal = EMA(PPO, signal_period)
///   Hist = PPO - Signal
///
/// 跟 MACD 的差別：PPO 用百分比、可跨 symbol 比較（MACD 是絕對值、BTC 跟 ETH 不能直接比）。
/// 同樣的 12/26/9 預設值。
///
/// 解讀：
///   PPO &gt; 0 + 上穿 Signal = 多頭動能加強
///   PPO &lt; 0 + 下穿 Signal = 空頭動能加強
///   Hist 翻號 = 早期轉折提示
/// </summary>
public static class Ppo
{
    public class Result
    {
        public decimal Value     { get; init; }   // PPO 主線（百分比）
        public decimal Signal    { get; init; }   // signal 線
        public decimal Histogram { get; init; }   // value - signal
    }

    public static Result? Compute(List<BarData> bars, int shortPeriod = 12, int longPeriod = 26, int signalPeriod = 9)
    {
        if (bars == null) return null;
        var minBars = longPeriod + signalPeriod;
        if (bars.Count < minBars) return null;

        var closes = bars.Select(b => b.Close).ToList();
        var emaShort = ComputeEmaSeries(closes, shortPeriod);
        var emaLong  = ComputeEmaSeries(closes, longPeriod);

        // 對齊兩條 EMA 的索引（emaShort 比 emaLong 多 (longPeriod - shortPeriod) 個值）
        var offset = emaShort.Count - emaLong.Count;
        var ppoSeries = new List<decimal>(emaLong.Count);
        for (int i = 0; i < emaLong.Count; i++)
        {
            var s = emaShort[i + offset];
            var l = emaLong[i];
            if (l == 0m) ppoSeries.Add(0m);
            else ppoSeries.Add((s - l) / l * 100m);
        }

        var signalSeries = ComputeEmaSeries(ppoSeries, signalPeriod);
        if (signalSeries.Count == 0) return null;

        var ppoNow    = ppoSeries[^1];
        var signalNow = signalSeries[^1];
        return new Result
        {
            Value     = Math.Round(ppoNow, 6),
            Signal    = Math.Round(signalNow, 6),
            Histogram = Math.Round(ppoNow - signalNow, 6),
        };
    }

    private static List<decimal> ComputeEmaSeries(List<decimal> values, int period)
    {
        var result = new List<decimal>();
        if (values.Count < period) return result;

        var k = 2m / (period + 1);
        decimal seed = 0m;
        for (int i = 0; i < period; i++) seed += values[i];
        seed /= period;
        result.Add(seed);

        for (int i = period; i < values.Count; i++)
        {
            var ema = values[i] * k + result[^1] * (1m - k);
            result.Add(ema);
        }
        return result;
    }
}
