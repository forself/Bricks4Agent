namespace StrategyWorker.Engine.Indicators;

/// <summary>
/// Williams %R — Larry Williams 的反轉動能指標、Stochastic 的鏡像版本：
///   %R = -100 × (HighestHigh(n) - close[t]) / (HighestHigh(n) - LowestLow(n))
///
/// 解讀：
///   %R &gt; -20 → 超買、可能回落
///   %R &lt; -80 → 超賣、可能反彈
///   穿越 -50 = 中軸翻轉訊號
///
/// 跟 Stochastic 的差別：方向相反（這個是 -100..0、Stochastic 是 0..100），
/// 但訊號邏輯一致。Williams %R 對短期極端更敏感、適合震盪策略。
///
/// 預設 period=14。
/// </summary>
public static class WilliamsR
{
    public static decimal? Compute(List<BarData> bars, int period = 14)
    {
        if (bars == null || bars.Count < period) return null;

        var window = bars.Count >= period ? bars[^period..] : bars;
        decimal highest = decimal.MinValue, lowest = decimal.MaxValue;
        foreach (var b in window)
        {
            if (b.High > highest) highest = b.High;
            if (b.Low  < lowest)  lowest  = b.Low;
        }
        var range = highest - lowest;
        if (range == 0m) return -50m;   // 完全 flat：給中性值不要除零

        var close = bars[^1].Close;
        return Math.Round(-100m * (highest - close) / range, 4);
    }
}
