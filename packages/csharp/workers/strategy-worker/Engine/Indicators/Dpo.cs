namespace StrategyWorker.Engine.Indicators;

/// <summary>
/// DPO (Detrended Price Oscillator) — 過濾長期趨勢、放大循環/震盪：
///   shift = period/2 + 1
///   DPO[t] = close[t - shift] - SMA(close, period)[t]
///
/// 重要：classical formula 把 close 拿過去某根（t - shift）跟「當下的 SMA」相減。
/// 雖然名字裡有 displaced、實作只看過去資料、**無 look-ahead bias**。
/// 跟 Pring 在《Technical Analysis Explained》的定義一致。
///
/// 解讀：
///   DPO &gt; 0 → 價格 (shift 期前) 高於 SMA、短期偏多
///   DPO &lt; 0 → 短期偏空
///   過 0 線翻轉 = 短期循環轉折提示
///
/// 適合配合趨勢指標使用（在 trending market DPO 雜訊大、在 ranging market 最有效）。
/// 預設 period=20。
/// </summary>
public static class Dpo
{
    public static decimal? Compute(List<BarData> bars, int period = 20)
    {
        if (bars == null) return null;
        var shift = period / 2 + 1;
        // 需要 period 個 bar 算 SMA + shift 個 bar 往回看
        if (bars.Count < period + shift) return null;

        // SMA 用最後 period 根算
        decimal sum = 0m;
        for (int i = bars.Count - period; i < bars.Count; i++) sum += bars[i].Close;
        var sma = sum / period;

        var shiftedClose = bars[bars.Count - 1 - shift].Close;
        return Math.Round(shiftedClose - sma, 6);
    }
}
