using StrategyWorker.Models;

namespace StrategyWorker.Engine.Indicators;

/// <summary>
/// 資金費率 / 未平倉量偏向(Funding & Open Interest Bias)—— 永續合約的「持倉擁擠度」,與價格方向正交。
///
/// 方向指標看價格本身,但看不到「市場資金站在哪一邊、有多擠」:
///   funding rate 高(正)= 多頭付錢給空頭 = 多頭擁擠 → 過熱、潛在 contrarian 反轉向下。
///   funding rate 低(負)= 空頭擁擠 → 過熱向上。
///   OI 變化:OI 增 = 有新資金進場(趨勢有續航);OI 減 = 平倉/動能衰竭。
///
/// 資料來源:perp 的 funding/OI(BingX/Binance 等的 swap 公開端點即可,免 API key)。
/// 由 caller 把 funding_rate/open_interest 填進 BarData(現在無資料時整條回 null、上層自動降級)。
/// 設計對標朋友 ai-quant-starter2/app/services/perp_metrics.py(ccxt swap、概念移植、C# 重寫)。
/// </summary>
public static class FundingBias
{
    /// <summary>回傳 (最新資金費率, 資金費率百分位 0..1, OI 變化%);無 funding 資料回 null。</summary>
    public static (decimal FundingRate, decimal FundingPercentile, decimal OiChangePct)? Compute(
        List<BarData> bars, int lookback = 100)
    {
        if (bars == null || bars.Count < 20) return null;
        int n = Math.Min(lookback, bars.Count);
        var window = bars.GetRange(bars.Count - n, n);

        // 收集有 funding 的 bar;太少就降級(代表這 symbol 沒接 perp 資料)
        var fundings = new List<decimal>();
        foreach (var b in window) if (b.FundingRate.HasValue) fundings.Add(b.FundingRate.Value);
        if (fundings.Count < 10) return null;

        decimal current = fundings[^1];
        int leq = 0;
        foreach (var f in fundings) if (f <= current) leq++;
        decimal pct = (decimal)leq / fundings.Count;  // 當前資金費率在近期分布的位置

        // OI 變化%(窗口內第一個有值 → 最後一個有值)
        decimal oiChange = 0m;
        decimal? firstOi = null, lastOi = null;
        foreach (var b in window)
            if (b.OpenInterest.HasValue) { firstOi ??= b.OpenInterest; lastOi = b.OpenInterest; }
        if (firstOi is > 0m && lastOi.HasValue)
            oiChange = Math.Round((lastOi.Value - firstOi.Value) / firstOi.Value * 100m, 4);

        return (Math.Round(current, 6), Math.Round(pct, 4), oiChange);
    }
}
