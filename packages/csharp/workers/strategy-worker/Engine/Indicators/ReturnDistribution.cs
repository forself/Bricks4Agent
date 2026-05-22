using StrategyWorker.Models;

namespace StrategyWorker.Engine.Indicators;

/// <summary>
/// 報酬分布形狀(偏度 Skew + 超額峰度 Kurtosis)—— 衡量下行尾部風險,與方向、波動率水平都正交。
///
/// 方向指標看「往哪走」、波動率看二階矩(離散度),這裡看三/四階矩(分布形狀):
///   Skew &lt; 0(負偏)→ 左尾長:大跌比大漲頻繁/劇烈 → 結構脆弱。
///   Kurtosis &gt; 0(超額峰度、肥尾)→ 極端事件比常態多 → 暴漲暴跌風險高。
/// 負偏 + 肥尾 = 典型的崩盤前體質(crypto 特別常見)。純收盤對數報酬可算,不需新資料源。
/// </summary>
public static class ReturnDistribution
{
    /// <summary>回傳 (偏度, 超額峰度);資料不足或序列平坦回 null。</summary>
    public static (decimal Skew, decimal Kurtosis)? Compute(List<BarData> bars, int lookback = 100)
    {
        if (bars == null || bars.Count < 32) return null;
        int n = Math.Min(lookback, bars.Count);

        // 對數報酬序列(與 Hurst 同樣的窗口構造)
        var series = new double[n - 1];
        for (int i = 0; i < n - 1; i++)
        {
            double prev = (double)bars[bars.Count - n + i].Close;
            double cur  = (double)bars[bars.Count - n + i + 1].Close;
            series[i] = prev > 0 ? Math.Log(cur / prev) : 0;
        }
        int len = series.Length;
        if (len < 16) return null;

        double mean = 0;
        foreach (var v in series) mean += v;
        mean /= len;

        double m2 = 0, m3 = 0, m4 = 0;
        foreach (var v in series)
        {
            double d  = v - mean;
            double d2 = d * d;
            m2 += d2;
            m3 += d2 * d;
            m4 += d2 * d2;
        }
        m2 /= len; m3 /= len; m4 /= len;

        double std = Math.Sqrt(m2);
        if (std < 1e-12) return null;  // 完全平坦、無分布形狀可言

        double skew = m3 / (std * std * std);
        double kurt = m4 / (m2 * m2) - 3.0;  // 超額峰度(常態 = 0)

        return (Math.Round((decimal)skew, 4), Math.Round((decimal)kurt, 4));
    }
}
