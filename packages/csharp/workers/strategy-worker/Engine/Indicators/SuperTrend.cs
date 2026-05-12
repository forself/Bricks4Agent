using StrategyWorker.Models;

namespace StrategyWorker.Engine.Indicators;

/// <summary>
/// SuperTrend——ATR-based 動態趨勢線、現代趨勢跟隨指標。
///
/// 公式：
///   TR             = max(H-L, |H-prevC|, |L-prevC|)
///   ATR            = SMA(TR, atrPeriod)
///   basic_upper[i] = (H[i]+L[i])/2 + multiplier × ATR[i]
///   basic_lower[i] = (H[i]+L[i])/2 - multiplier × ATR[i]
///
/// 「final」上下軌帶記憶（path-dependent）：
///   final_upper[i] = basic_upper[i] 若 basic_upper[i] < final_upper[i-1]
///                                    OR close[i-1] > final_upper[i-1]
///                    否則 final_upper[i-1]
///   final_lower[i] 對稱
///
/// 趨勢方向：
///   trend[i] = +1 若 close[i] > final_upper[i-1]
///              -1 若 close[i] < final_lower[i-1]
///              否則 trend[i-1]
///
///   supertrend[i] = trend[i]==+1 ? final_lower[i] : final_upper[i]
///
/// 解讀：
///   - 收盤 > supertrend → 多頭、線扮演動態停損
///   - 收盤 < supertrend → 空頭、反過來
///   - 距離 (price - st) / price 越大代表離反轉越遠（順勢）
///   - 距離很小（< 0.5%）代表接近反轉、要小心
///
/// 設計參考：朋友 ai-quant-starter2/app/services/strategy_selector.py:s_supertrend。
/// </summary>
public static class SuperTrend
{
    public class Result
    {
        public decimal Value      { get; init; }   // supertrend 當前值
        public int     Trend      { get; init; }   // +1 多 / -1 空
        public decimal DistancePct{ get; init; }   // |price - st| / price × 100
    }

    public static Result? Compute(List<BarData> bars, int atrPeriod = 10, decimal multiplier = 3m)
    {
        // ATR 需要 atrPeriod 根 TR、TR 需要 prevClose、所以 ≥ atrPeriod+1 才有第一個 ATR
        if (bars == null || bars.Count < atrPeriod + 1) return null;

        var n = bars.Count;
        var tr = new decimal[n];
        for (int i = 1; i < n; i++)
        {
            var b = bars[i];
            var prevClose = bars[i - 1].Close;
            tr[i] = Math.Max(
                b.High - b.Low,
                Math.Max(Math.Abs(b.High - prevClose), Math.Abs(b.Low - prevClose))
            );
        }

        // ATR(atrPeriod) using SMA over TR
        var atr = new decimal[n];
        for (int i = atrPeriod; i < n; i++)
        {
            decimal sum = 0m;
            for (int k = i - atrPeriod + 1; k <= i; k++) sum += tr[k];
            atr[i] = sum / atrPeriod;
        }

        // basic + final upper/lower with path-dependent carry-forward
        var finalUpper = new decimal[n];
        var finalLower = new decimal[n];
        var trend      = new int[n];
        var st         = new decimal[n];

        // 第一個有效 index = atrPeriod；之前的不算
        for (int i = atrPeriod; i < n; i++)
        {
            var hl2 = (bars[i].High + bars[i].Low) / 2m;
            var bUpper = hl2 + multiplier * atr[i];
            var bLower = hl2 - multiplier * atr[i];

            if (i == atrPeriod)
            {
                finalUpper[i] = bUpper;
                finalLower[i] = bLower;
                trend[i]      = bars[i].Close >= hl2 ? 1 : -1;
            }
            else
            {
                // upper：若 basic 比前一條 final 緊（更低）就更新；或前一根收盤已突破上方就重置
                finalUpper[i] = (bUpper < finalUpper[i - 1] || bars[i - 1].Close > finalUpper[i - 1])
                    ? bUpper
                    : finalUpper[i - 1];
                // lower：若 basic 比前一條 final 緊（更高）就更新；或前一根收盤已跌破下方就重置
                finalLower[i] = (bLower > finalLower[i - 1] || bars[i - 1].Close < finalLower[i - 1])
                    ? bLower
                    : finalLower[i - 1];

                // trend：以「當前 close 跨越前一根 final 軌」判定
                if (bars[i].Close > finalUpper[i - 1]) trend[i] = 1;
                else if (bars[i].Close < finalLower[i - 1]) trend[i] = -1;
                else trend[i] = trend[i - 1];
            }
            st[i] = trend[i] == 1 ? finalLower[i] : finalUpper[i];
        }

        var lastIdx = n - 1;
        var price = bars[lastIdx].Close;
        var stv   = st[lastIdx];
        var dist  = price == 0 ? 0m : Math.Abs(price - stv) / price * 100m;

        return new Result
        {
            Value       = Math.Round(stv, 6),
            Trend       = trend[lastIdx],
            DistancePct = Math.Round(dist, 4),
        };
    }
}
