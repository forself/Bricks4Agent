namespace StrategyWorker.Engine.Indicators;

/// <summary>
/// TRIX — 三重指數平滑後的動量變化率：
///   EMA1 = EMA(close, n)
///   EMA2 = EMA(EMA1, n)
///   EMA3 = EMA(EMA2, n)
///   TRIX = (EMA3[t] - EMA3[t-1]) / EMA3[t-1] × 100
///
/// 三重 EMA 把短週期雜訊濾掉、只剩主要動能轉折；變化率取代純值是為了 normalize、
/// 跨 symbol / 時間框架可比較。TRIX 過 0 線通常被當成「趨勢翻轉」訊號。
///
/// 預設 period=15（Genesis Software 原版設定）。需要至少 3n+1 根 bar 才能算出。
/// </summary>
public static class Trix
{
    public static decimal? Compute(List<BarData> bars, int period = 15)
    {
        if (bars == null || bars.Count < period * 3 + 1) return null;

        // 第一層 EMA
        var ema1 = ComputeEmaSeries(bars.Select(b => b.Close).ToList(), period);
        // 第二層 EMA on ema1
        var ema2 = ComputeEmaSeries(ema1, period);
        // 第三層 EMA on ema2
        var ema3 = ComputeEmaSeries(ema2, period);

        if (ema3.Count < 2) return null;
        var curr = ema3[^1];
        var prev = ema3[^2];
        if (prev == 0m) return null;

        return Math.Round((curr - prev) / prev * 100m, 6);
    }

    /// <summary>
    /// 標準 EMA series：第一個值用前 period 個值的 SMA seed、之後遞推。
    /// 回傳長度 = input.Count - period + 1（前 period-1 個 warm-up bar 沒有 EMA）。
    /// </summary>
    private static List<decimal> ComputeEmaSeries(List<decimal> values, int period)
    {
        var result = new List<decimal>();
        if (values.Count < period) return result;

        var k = 2m / (period + 1);
        // SMA seed
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
