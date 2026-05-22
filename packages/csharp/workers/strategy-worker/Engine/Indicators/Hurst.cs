using System;
using System.Collections.Generic;
using System.Linq;
using StrategyWorker.Models;

namespace StrategyWorker.Engine.Indicators;

/// <summary>
/// Hurst 指數(R/S rescaled range)—— 判斷市場是「趨勢」還是「均值回歸」,與方向指標正交。
///   H &gt; 0.5 → 趨勢延續(持續性);H &lt; 0.5 → 均值回歸(反持續);H ≈ 0.5 → 隨機漫步。
/// 對多個切割尺度做 log(R/S) ~ log(n) 線性回歸、取斜率 = H。純收盤對數報酬,不需新資料源。
/// </summary>
public static class Hurst
{
    /// <summary>回傳 Hurst H(約 0..1);資料不足回 null。</summary>
    public static decimal? Compute(List<BarData> bars, int lookback = 100)
    {
        if (bars == null || bars.Count < 32) return null;
        int n = Math.Min(lookback, bars.Count);

        // 對數報酬序列
        var series = new double[n - 1];
        for (int i = 0; i < n - 1; i++)
        {
            double prev = (double)bars[bars.Count - n + i].Close;
            double cur = (double)bars[bars.Count - n + i + 1].Close;
            series[i] = prev > 0 ? Math.Log(cur / prev) : 0;
        }
        int len = series.Length;
        if (len < 16) return null;

        // 多尺度 R/S → 回歸
        var xs = new List<double>();
        var ys = new List<double>();
        for (int chunk = len; chunk >= 8; chunk /= 2)
        {
            int numChunks = len / chunk;
            if (numChunks < 1) break;
            double rsSum = 0; int cnt = 0;
            for (int c = 0; c < numChunks; c++)
            {
                double mean = 0;
                for (int i = 0; i < chunk; i++) mean += series[c * chunk + i];
                mean /= chunk;

                double cum = 0, min = double.MaxValue, max = double.MinValue, sq = 0;
                for (int i = 0; i < chunk; i++)
                {
                    double d = series[c * chunk + i] - mean;
                    cum += d;
                    if (cum < min) min = cum;
                    if (cum > max) max = cum;
                    sq += d * d;
                }
                double std = Math.Sqrt(sq / chunk);
                double range = max - min;
                if (std > 1e-12 && range > 0) { rsSum += range / std; cnt++; }
            }
            if (cnt > 0) { xs.Add(Math.Log(chunk)); ys.Add(Math.Log(rsSum / cnt)); }
        }
        if (xs.Count < 2) return null;

        double mx = xs.Average(), my = ys.Average(), num = 0, den = 0;
        for (int i = 0; i < xs.Count; i++) { num += (xs[i] - mx) * (ys[i] - my); den += (xs[i] - mx) * (xs[i] - mx); }
        if (den < 1e-12) return null;

        double h = num / den;
        return Math.Round((decimal)Math.Clamp(h, 0.0, 1.0), 4);
    }
}
