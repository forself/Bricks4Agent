using StrategyWorker.Models;

namespace StrategyWorker.Engine.Indicators;

/// <summary>
/// 維加斯通道（Vegas Tunnel / Vegas Channel）純數學工具。
///
/// 維加斯通道是一套由多條 EMA 疊出來的趨勢跟隨系統，經典版本使用四條 EMA：
///   - 主通道（Main Tunnel）：EMA 144 與 EMA 169
///     → 這兩條 EMA 之間的區間就是「通道」本身，是中期交易區
///   - 長期趨勢過濾（Long Tunnel）：EMA 576 與 EMA 676
///     → 位在主通道上方 = 大多頭；位在下方 = 大空頭；盤整時交錯
///   - 觸發線（Trigger）：EMA 12（快速，用來判斷動能啟動）
///
/// 判讀邏輯：
///   1. 長通道 > 主通道 → 大趨勢向上；長通道 &lt; 主通道 → 大趨勢向下
///   2. 大多頭中，價格若回檔到主通道（EMA144/169 之間）且 EMA12 由下向上穿越
///      主通道 → 順勢買進；反之空頭中反向做空
///   3. 通道寬度（上界 - 下界）/ 價格 可衡量當下波動，太窄代表趨勢未明
///
/// 為何使用 EMA：比 SMA 更快反應最近價格，短期噪音較少；且費波那契數列的週期（144/169/576/676）
/// 被實務交易者驗證在股票與外匯的日線/4小時線都能運作，是經典設定。
///
/// 注意：經典參數需要至少 676 根 K 線才能完整計算，若資料不足會回傳 null。
/// 呼叫方可傳入較小的 period 組合（例如 34/55/144/233）做 compact 版本。
/// </summary>
public static class VegasTunnel
{
    /// <summary>維加斯通道快照：四條 EMA 與派生訊息。</summary>
    public class Snapshot
    {
        public decimal MainFastEma   { get; init; }   // EMA 144
        public decimal MainSlowEma   { get; init; }   // EMA 169
        public decimal LongFastEma   { get; init; }   // EMA 576
        public decimal LongSlowEma   { get; init; }   // EMA 676
        public decimal TriggerEma    { get; init; }   // EMA 12

        public decimal TunnelUpper   { get; init; }   // max(MainFast, MainSlow)
        public decimal TunnelLower   { get; init; }   // min(MainFast, MainSlow)
        public decimal TunnelWidthPct { get; init; }  // (Upper - Lower) / price × 100

        /// <summary>大趨勢：+1 = 多頭（長通道在主通道上方）、-1 = 空頭、0 = 糾結</summary>
        public int MacroTrend { get; init; }

        /// <summary>價格相對主通道：+1 = 在通道上方、0 = 在通道內、-1 = 在通道下方</summary>
        public int PriceZone { get; init; }

        /// <summary>EMA12 是否剛穿越主通道：+1 = 由下往上穿越、-1 = 由上往下、0 = 未發生</summary>
        public int TriggerCross { get; init; }
    }

    /// <summary>
    /// 計算維加斯通道快照。資料不足則回傳 null。
    /// </summary>
    public static Snapshot? Compute(
        List<BarData> bars,
        int mainFast = 144,
        int mainSlow = 169,
        int longFast = 576,
        int longSlow = 676,
        int triggerPeriod = 12)
    {
        if (bars == null || bars.Count == 0) return null;
        var needed = Math.Max(longSlow, Math.Max(mainSlow, triggerPeriod));
        if (bars.Count < needed) return null;

        var closes = new decimal[bars.Count];
        for (int i = 0; i < bars.Count; i++) closes[i] = bars[i].Close;

        var mainFastSeries = ComputeEma(closes, mainFast);
        var mainSlowSeries = ComputeEma(closes, mainSlow);
        var longFastSeries = ComputeEma(closes, longFast);
        var longSlowSeries = ComputeEma(closes, longSlow);
        var triggerSeries  = ComputeEma(closes, triggerPeriod);

        var last = closes.Length - 1;
        var prev = last - 1;
        var price = closes[last];

        var mainFastNow  = mainFastSeries[last];
        var mainSlowNow  = mainSlowSeries[last];
        var longFastNow  = longFastSeries[last];
        var longSlowNow  = longSlowSeries[last];
        var triggerNow   = triggerSeries[last];
        var triggerPrev  = prev >= 0 ? triggerSeries[prev] : triggerNow;

        var tunnelUpper = Math.Max(mainFastNow, mainSlowNow);
        var tunnelLower = Math.Min(mainFastNow, mainSlowNow);
        var widthPct = price == 0 ? 0m : (tunnelUpper - tunnelLower) / price * 100m;

        // 大趨勢：比較長短兩通道的「平均線」位置
        var longCenter = (longFastNow + longSlowNow) / 2m;
        var mainCenter = (mainFastNow + mainSlowNow) / 2m;
        var macro = 0;
        var gap = longCenter == 0 ? 0m : Math.Abs(longCenter - mainCenter) / longCenter;
        if (gap < 0.005m)              macro = 0;     // 兩通道糾結
        else if (longCenter < mainCenter) macro = 1;   // 主通道在長通道上方 = 多頭
        else                           macro = -1;    // 空頭

        // 價格相對主通道的位置
        int zone;
        if (price > tunnelUpper)      zone = 1;
        else if (price < tunnelLower) zone = -1;
        else                          zone = 0;

        // 觸發線穿越主通道中軸
        var crossNow = triggerNow - mainCenter;
        var crossPrev = triggerPrev - ((mainFastSeries[prev] + mainSlowSeries[prev]) / 2m);
        int trigger = 0;
        if (crossPrev <= 0 && crossNow > 0)      trigger = 1;
        else if (crossPrev >= 0 && crossNow < 0) trigger = -1;

        return new Snapshot
        {
            MainFastEma    = Math.Round(mainFastNow, 4),
            MainSlowEma    = Math.Round(mainSlowNow, 4),
            LongFastEma    = Math.Round(longFastNow, 4),
            LongSlowEma    = Math.Round(longSlowNow, 4),
            TriggerEma     = Math.Round(triggerNow, 4),
            TunnelUpper    = Math.Round(tunnelUpper, 4),
            TunnelLower    = Math.Round(tunnelLower, 4),
            TunnelWidthPct = Math.Round(widthPct, 4),
            MacroTrend     = macro,
            PriceZone      = zone,
            TriggerCross   = trigger,
        };
    }

    /// <summary>
    /// EMA 序列：seed 使用前 period 根的 SMA，之後用遞迴公式 EMA_t = α·P_t + (1-α)·EMA_{t-1}。
    /// α = 2 / (period + 1)。
    /// </summary>
    private static decimal[] ComputeEma(decimal[] closes, int period)
    {
        var result = new decimal[closes.Length];
        if (period <= 0 || closes.Length == 0) return result;

        if (closes.Length < period)
        {
            // 資料不足，以最後一根收盤價回填，避免 caller 誤用
            for (int i = 0; i < closes.Length; i++) result[i] = closes[i];
            return result;
        }

        decimal sum = 0m;
        for (int i = 0; i < period; i++) sum += closes[i];
        var sma = sum / period;
        for (int i = 0; i < period; i++) result[i] = sma;

        var alpha = 2m / (period + 1m);
        for (int i = period; i < closes.Length; i++)
        {
            result[i] = alpha * closes[i] + (1m - alpha) * result[i - 1];
        }
        return result;
    }
}
