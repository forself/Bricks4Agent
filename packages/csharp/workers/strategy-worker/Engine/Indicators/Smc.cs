using StrategyWorker.Models;

namespace StrategyWorker.Engine.Indicators;

/// <summary>
/// SMC（Smart Money Concepts）—— 機構派價格結構訊號。
///
/// 偵測四類核心結構（純價格、不需新資料源）：
///   - BOS  (Break of Structure)   : close 突破前一個「已確認」swing → 趨勢延續
///   - CHoCH (Change of Character)  : 反向突破前一個 swing → 趨勢轉變（早期反轉訊號）
///   - Order Block (OB)            : 結構破壞前最後一根反向 K → 機構吃單區（支撐/壓力）
///   - Fair Value Gap (FVG)        : 連續 3 根 K 的未填補缺口 → 價格傾向回補
///
/// 進場模型（SMC 經典「順結構回測 OB/FVG」）：
///   多頭結構 + 價格回踩到「仍有效」的 bullish OB / bullish FVG 區 → buy
///   空頭結構 + 價格反彈到 bearish OB / bearish FVG 區             → sell
///   剛發生 CHoCH（≤ 數根內）本身可當早期反轉訊號。
///
/// No-lookahead 保證：swing pivot 需前後各 window 根才「確認」，所以一個 pivot 只在
/// 它的 window 完全過去後才會被 break 偵測引用（pivot.index + window &lt; breakBar）。
/// 全部只讀傳入 bars 範圍內、不碰未來。
///
/// 設計概念對照：ai-quant-starter2/app/services/smc_engine.py（僅參考演算法、C# 重寫）。
/// </summary>
public static class Smc
{
    public sealed class SmcState
    {
        public string Trend          { get; set; } = "neutral"; // up / down / neutral
        public string BreakType      { get; set; } = "none";    // BOS_Up / BOS_Down / CHoCH_Up / CHoCH_Down / none
        public int    BarsSinceBreak { get; set; } = -1;

        public string  Signal     { get; set; } = "hold";       // buy / sell / hold
        public string  SignalType { get; set; } = "none";       // OB_Retest / FVG_Retest / CHoCH / none
        public decimal Confidence { get; set; }                 // 0..1
        public decimal ZoneLow    { get; set; }
        public decimal ZoneHigh   { get; set; }
        public string  Reason     { get; set; } = "";

        public int ActiveBullObCount  { get; set; }
        public int ActiveBearObCount  { get; set; }
        public int ActiveBullFvgCount { get; set; }
        public int ActiveBearFvgCount { get; set; }
    }

    private readonly record struct Pivot(int Index, bool IsHigh, decimal Price);
    private readonly record struct Break(int Index, bool Up, bool IsChoch, decimal RefPrice);
    private readonly record struct Zone(int Index, bool Bullish, decimal Low, decimal High, bool Active);

    // 回測區的「currently retesting」容差（zone 邊界 ±0.3%）
    private const decimal RetestTol = 0.003m;
    // CHoCH 視為「剛發生」的最大 bar 距離
    private const int FreshChochBars = 3;

    public static SmcState Detect(List<BarData> bars, int pivotWindow = 3)
    {
        var st = new SmcState();
        if (bars == null || bars.Count < pivotWindow * 2 + 3) return st;

        int n = bars.Count;
        var pivots = SwingPivots(bars, pivotWindow);
        var breaks = DetectBreaks(bars, pivots, pivotWindow);

        // ── 當前結構趨勢 = 最近一次 break ──
        if (breaks.Count > 0)
        {
            var last = breaks[^1];
            st.Trend = last.Up ? "up" : "down";
            st.BreakType = (last.IsChoch, last.Up) switch
            {
                (true, true)   => "CHoCH_Up",
                (true, false)  => "CHoCH_Down",
                (false, true)  => "BOS_Up",
                (false, false) => "BOS_Down",
            };
            st.BarsSinceBreak = (n - 1) - last.Index;
        }

        var obs  = DetectOrderBlocks(bars, breaks);
        var fvgs = DetectFvgs(bars);

        st.ActiveBullObCount  = obs.Count(z => z.Bullish && z.Active);
        st.ActiveBearObCount  = obs.Count(z => !z.Bullish && z.Active);
        st.ActiveBullFvgCount = fvgs.Count(z => z.Bullish && z.Active);
        st.ActiveBearFvgCount = fvgs.Count(z => !z.Bullish && z.Active);

        var price = bars[^1].Close;
        bool trendUp = st.Trend == "up";
        bool trendDown = st.Trend == "down";

        // ── 進場判斷：順結構回測 OB（優先）→ FVG → 剛發生的 CHoCH ──
        if (trendUp || trendDown)
        {
            // 1) OB 回測（最強）
            var ob = NearestRetest(trendUp ? obs.Where(z => z.Bullish && z.Active)
                                           : obs.Where(z => !z.Bullish && z.Active), price);
            if (ob.HasValue)
            {
                Fill(st, trendUp ? "buy" : "sell", "OB_Retest", 0.72m, ob.Value,
                    $"{(trendUp ? "多" : "空")}頭結構（{st.BreakType}）+ 價 {price:F4} 回測有效 {(trendUp ? "bullish" : "bearish")} OB [{ob.Value.Low:F4},{ob.Value.High:F4}]");
                return st;
            }

            // 2) FVG 回測
            var fvg = NearestRetest(trendUp ? fvgs.Where(z => z.Bullish && z.Active)
                                            : fvgs.Where(z => !z.Bullish && z.Active), price);
            if (fvg.HasValue)
            {
                Fill(st, trendUp ? "buy" : "sell", "FVG_Retest", 0.66m, fvg.Value,
                    $"{(trendUp ? "多" : "空")}頭結構（{st.BreakType}）+ 價 {price:F4} 回測未填補 {(trendUp ? "bullish" : "bearish")} FVG [{fvg.Value.Low:F4},{fvg.Value.High:F4}]");
                return st;
            }
        }

        // 3) 剛發生的 CHoCH = 早期反轉
        if ((st.BreakType == "CHoCH_Up" || st.BreakType == "CHoCH_Down")
            && st.BarsSinceBreak >= 0 && st.BarsSinceBreak <= FreshChochBars)
        {
            var dir = st.BreakType == "CHoCH_Up";
            st.Signal = dir ? "buy" : "sell";
            st.SignalType = "CHoCH";
            st.Confidence = 0.70m;
            st.Reason = $"剛發生 {st.BreakType}（{st.BarsSinceBreak} 根前）— 結構轉{(dir ? "多" : "空")}早期訊號";
            return st;
        }

        st.Reason = breaks.Count > 0
            ? $"結構={st.BreakType}（{st.BarsSinceBreak} 根前）但價未回測任何有效 OB/FVG — 觀望"
            : "尚無明確結構破壞 — 觀望";
        return st;
    }

    private static void Fill(SmcState st, string sig, string type, decimal conf, Zone z, string reason)
    {
        st.Signal = sig;
        st.SignalType = type;
        st.Confidence = conf;
        st.ZoneLow = z.Low;
        st.ZoneHigh = z.High;
        st.Reason = reason;
    }

    /// <summary>找價格目前正在回測（落在 zone ±容差內）、且最靠近現價的 zone。</summary>
    private static Zone? NearestRetest(IEnumerable<Zone> zones, decimal price)
    {
        Zone? best = null;
        decimal bestDist = decimal.MaxValue;
        foreach (var z in zones)
        {
            var lo = z.Low * (1m - RetestTol);
            var hi = z.High * (1m + RetestTol);
            if (price < lo || price > hi) continue;
            var mid = (z.Low + z.High) / 2m;
            var dist = Math.Abs(price - mid);
            if (dist < bestDist) { bestDist = dist; best = z; }
        }
        return best;
    }

    // ── Swing pivots（fractal）──
    private static List<Pivot> SwingPivots(List<BarData> bars, int window)
    {
        var outp = new List<Pivot>();
        int n = bars.Count;
        for (int i = window; i < n - window; i++)
        {
            decimal hi = bars[i].High, lo = bars[i].Low;
            bool isHigh = true, isLow = true;
            for (int k = i - window; k <= i + window; k++)
            {
                if (bars[k].High > hi) isHigh = false;
                if (bars[k].Low  < lo) isLow  = false;
            }
            if (isHigh) outp.Add(new Pivot(i, true, hi));
            else if (isLow) outp.Add(new Pivot(i, false, lo));
        }
        return outp;
    }

    // ── BOS / CHoCH ──
    private static List<Break> DetectBreaks(List<BarData> bars, List<Pivot> pivots, int window)
    {
        var outb = new List<Break>();
        int n = bars.Count;
        if (pivots.Count < 2) return outb;
        bool? lastUp = null;

        for (int i = window + 1; i < n; i++)
        {
            // 只引用「已完全確認」的 pivot：pivot.index + window < i
            Pivot? prevHigh = null, prevLow = null;
            for (int j = pivots.Count - 1; j >= 0; j--)
            {
                var p = pivots[j];
                if (p.Index + window >= i) continue;
                if (p.IsHigh && prevHigh == null) prevHigh = p;
                else if (!p.IsHigh && prevLow == null) prevLow = p;
                if (prevHigh != null && prevLow != null) break;
            }
            if (prevHigh == null || prevLow == null) continue;

            var c = bars[i].Close;
            bool brokeHigh = c > prevHigh.Value.Price;
            bool brokeLow  = c < prevLow.Value.Price;
            if (brokeHigh == brokeLow) continue;   // 都沒破 or 異常雙破 → 跳過

            bool up = brokeHigh;
            bool isChoch = lastUp.HasValue && lastUp.Value != up;
            lastUp = up;
            outb.Add(new Break(i, up, isChoch, up ? prevHigh.Value.Price : prevLow.Value.Price));
        }
        return outb;
    }

    // ── Order Blocks（結構破壞前最後一根反向 K）──
    private static List<Zone> DetectOrderBlocks(List<BarData> bars, List<Break> breaks)
    {
        var outz = new List<Zone>();
        int n = bars.Count;
        foreach (var ev in breaks)
        {
            int obIdx = -1;
            int from = ev.Index - 1, to = Math.Max(0, ev.Index - 15);
            for (int j = from; j >= to; j--)
            {
                bool down = bars[j].Close < bars[j].Open;
                bool upK  = bars[j].Close > bars[j].Open;
                if (ev.Up && down) { obIdx = j; break; }   // bullish OB = BOS 前最後一根下跌 K
                if (!ev.Up && upK) { obIdx = j; break; }   // bearish OB = 前最後一根上漲 K
            }
            if (obIdx < 0) continue;

            decimal obHigh = bars[obIdx].High, obLow = bars[obIdx].Low;
            // active：結構破壞後到最後一根、close 沒「穿透」OB（多單 OB 沒被跌破、空單沒被漲破）
            bool active = true;
            for (int k = ev.Index + 1; k < n; k++)
            {
                if (ev.Up && bars[k].Close < obLow)  { active = false; break; }
                if (!ev.Up && bars[k].Close > obHigh) { active = false; break; }
            }
            outz.Add(new Zone(obIdx, ev.Up, obLow, obHigh, active));
        }
        return outz;
    }

    // ── Fair Value Gaps（3 根 K 未填補缺口）──
    private static List<Zone> DetectFvgs(List<BarData> bars)
    {
        var outz = new List<Zone>();
        int n = bars.Count;
        for (int i = 2; i < n; i++)
        {
            // bullish gap：bar[i-2].high < bar[i].low
            if (bars[i - 2].High < bars[i].Low)
            {
                decimal gapLow = bars[i - 2].High, gapHigh = bars[i].Low;
                bool active = true;
                for (int k = i + 1; k < n; k++)
                    if (bars[k].Close < gapLow) { active = false; break; }
                outz.Add(new Zone(i, true, gapLow, gapHigh, active));
            }
            // bearish gap：bar[i-2].low > bar[i].high
            else if (bars[i - 2].Low > bars[i].High)
            {
                decimal gapHigh = bars[i - 2].Low, gapLow = bars[i].High;
                bool active = true;
                for (int k = i + 1; k < n; k++)
                    if (bars[k].Close > gapHigh) { active = false; break; }
                outz.Add(new Zone(i, false, gapLow, gapHigh, active));
            }
        }
        return outz;
    }
}
