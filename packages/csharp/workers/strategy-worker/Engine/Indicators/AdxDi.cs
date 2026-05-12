using StrategyWorker.Models;

namespace StrategyWorker.Engine.Indicators;

/// <summary>
/// ADX + Directional Indicator (DMI / DI)。
///
/// 經典 Wilder ADX 用 Wilder smoothing（等效 EMA(α=1/N)）；本實作採朋友 repo 的
/// **rolling-mean 簡化版**——可解釋性高、且 truncation 後值穩定（純 SMA、no path）。
/// 對量化交易者熟悉的同行：值會跟 talib 略有差異、但訊號方向一致。
///
/// 公式：
///   +DM[i] = max(0, H[i] - H[i-1])    若 +DM > -DM、否則 0
///   -DM[i] = max(0, L[i-1] - L[i])    若 -DM > +DM、否則 0
///   TR     = max(H-L, |H-prevC|, |L-prevC|)
///   ATR14  = SMA(TR, 14)
///   +DI    = 100 × SMA(+DM, 14) / ATR14
///   -DI    = 100 × SMA(-DM, 14) / ATR14
///   DX     = 100 × |+DI - -DI| / (+DI + -DI)
///   ADX    = SMA(DX, 14)
///
/// 註：朋友的版本沒有對 +DM/-DM 之間做「只取較大方」的篩選、直接 clip(lower=0)、
/// 此處對齊朋友以保證可比。經典 Wilder 版可在後續另寫變體。
///
/// 解讀：
///   ADX > 25 → 趨勢強、可順勢
///   ADX < 20 → 震盪、避免趨勢策略
///   +DI > -DI → 多頭主導
///   |+DI - -DI| < 3 → DI 接近交叉、可能轉折
///
/// 設計參考：朋友 ai-quant-starter2/app/services/strategy_selector.py:s_adx_di。
/// </summary>
public static class AdxDi
{
    public class Result
    {
        public decimal Adx       { get; init; }
        public decimal PlusDi    { get; init; }
        public decimal MinusDi   { get; init; }
    }

    public static Result? Compute(List<BarData> bars, int period = 14)
    {
        // 至少需要 2×period 根（一次 SMA 算 DI、再一次 SMA 算 ADX）
        if (bars == null || bars.Count < period * 2 + 1) return null;

        var n = bars.Count;
        var tr      = new decimal[n];
        var plusDm  = new decimal[n];
        var minusDm = new decimal[n];

        for (int i = 1; i < n; i++)
        {
            var b = bars[i];
            var prev = bars[i - 1];
            tr[i] = Math.Max(
                b.High - b.Low,
                Math.Max(Math.Abs(b.High - prev.Close), Math.Abs(b.Low - prev.Close))
            );
            plusDm[i]  = Math.Max(0m, b.High - prev.High);
            minusDm[i] = Math.Max(0m, prev.Low - b.Low);
        }

        // SMA over rolling window for TR / +DM / -DM
        var atr      = new decimal[n];
        var plusDmS  = new decimal[n];
        var minusDmS = new decimal[n];
        for (int i = period; i < n; i++)
        {
            decimal sumTr = 0m, sumP = 0m, sumM = 0m;
            for (int k = i - period + 1; k <= i; k++)
            {
                sumTr += tr[k];
                sumP  += plusDm[k];
                sumM  += minusDm[k];
            }
            atr[i]      = sumTr / period;
            plusDmS[i]  = sumP  / period;
            minusDmS[i] = sumM  / period;
        }

        // +DI / -DI / DX
        var dx = new decimal[n];
        for (int i = period; i < n; i++)
        {
            if (atr[i] == 0m) continue;
            var pdi = 100m * plusDmS[i]  / atr[i];
            var mdi = 100m * minusDmS[i] / atr[i];
            var sum = pdi + mdi;
            if (sum == 0m) continue;
            dx[i] = 100m * Math.Abs(pdi - mdi) / sum;
        }

        // ADX = SMA(DX, period)，從 i = 2×period 起算才有滿 N 筆 DX
        var firstAdxIdx = period * 2;
        if (firstAdxIdx >= n) return null;
        var lastIdx = n - 1;

        decimal adxSum = 0m;
        for (int k = lastIdx - period + 1; k <= lastIdx; k++) adxSum += dx[k];
        var adx = adxSum / period;

        var lastPdi = atr[lastIdx] == 0m ? 0m : 100m * plusDmS[lastIdx]  / atr[lastIdx];
        var lastMdi = atr[lastIdx] == 0m ? 0m : 100m * minusDmS[lastIdx] / atr[lastIdx];

        return new Result
        {
            Adx     = Math.Round(adx, 4),
            PlusDi  = Math.Round(lastPdi, 4),
            MinusDi = Math.Round(lastMdi, 4),
        };
    }
}
