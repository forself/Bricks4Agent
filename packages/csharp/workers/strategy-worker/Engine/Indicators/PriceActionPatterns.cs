using StrategyWorker.Models;

namespace StrategyWorker.Engine.Indicators;

/// <summary>
/// Price Action（形態學）— 經典 K 線型態偵測。
///
/// 支援 6 種訊號（每一根 bar 視為「截至此刻」的觀測）：
///   - Engulfing       2 根：吞噬（後一根實體完全吞掉前一根）
///   - Pin Bar         1 根：Hammer（多） / Shooting Star（空），影線 ≥ 2 × 實體
///   - Inside Bar      2 根：當前高低點都在前一根範圍內（盤整等突破）
///   - Outside Bar     2 根：當前範圍包住前一根（劇烈轉變）
///   - Star            3 根：Morning Star（多反轉） / Evening Star（空反轉）
///   - Doji            1 根：實體 &lt; range 的 10%（猶豫）
///
/// 設計參考：朋友 ai-quant-starter2/app/services/price_action_engine.py。
/// 直接 port、行為對齊原版（含相同的 confidence 公式）。
/// </summary>
public static class PriceActionPatterns
{
    public enum Direction { Bullish, Bearish, Neutral }

    public class Detection
    {
        public string    Type       { get; init; } = "";
        public Direction Direction  { get; init; }
        public int       BarIndex   { get; init; }
        public decimal   Price      { get; init; }
        public decimal   Confidence { get; init; }
        public string    Reason     { get; init; } = "";
    }

    // ── 內部工具 ─────────────────────────────────────────────────

    private static decimal Body(BarData b)      => Math.Abs(b.Close - b.Open);
    private static decimal Range(BarData b)     => Math.Abs(b.High  - b.Low);
    private static decimal UpperWick(BarData b) => b.High - Math.Max(b.Open, b.Close);
    private static decimal LowerWick(BarData b) => Math.Min(b.Open, b.Close) - b.Low;

    // ── 偵測器 ──────────────────────────────────────────────────

    public static List<Detection> DetectEngulfing(List<BarData> bars)
    {
        var output = new List<Detection>();
        if (bars == null || bars.Count < 2) return output;
        for (int i = 1; i < bars.Count; i++)
        {
            var prev = bars[i - 1];
            var cur  = bars[i];
            var prevBody = Body(prev);
            var curBody  = Body(cur);
            if (prevBody == 0m || curBody == 0m) continue;

            // 當前實體完全吞掉前一根實體
            var prevBodyMin = Math.Min(prev.Open, prev.Close);
            var prevBodyMax = Math.Max(prev.Open, prev.Close);
            var curBodyMin  = Math.Min(cur.Open,  cur.Close);
            var curBodyMax  = Math.Max(cur.Open,  cur.Close);
            if (curBodyMin > prevBodyMin || curBodyMax < prevBodyMax) continue;

            var prevBear = prev.Close < prev.Open;
            var prevBull = prev.Close > prev.Open;
            var curBull  = cur.Close  > cur.Open;
            var curBear  = cur.Close  < cur.Open;

            var conf = Math.Min(0.95m, 0.55m + curBody / prevBody * 0.15m);

            if (prevBear && curBull)
            {
                output.Add(new Detection
                {
                    Type = "Bullish_Engulfing", Direction = Direction.Bullish,
                    BarIndex = i, Price = cur.Close, Confidence = Math.Round(conf, 3),
                    Reason = $"前空後多，當前實體 {curBody:F2} 完全吞噬前一根 {prevBody:F2}",
                });
            }
            else if (prevBull && curBear)
            {
                output.Add(new Detection
                {
                    Type = "Bearish_Engulfing", Direction = Direction.Bearish,
                    BarIndex = i, Price = cur.Close, Confidence = Math.Round(conf, 3),
                    Reason = $"前多後空，當前實體 {curBody:F2} 完全吞噬前一根 {prevBody:F2}",
                });
            }
        }
        return output;
    }

    public static List<Detection> DetectPinBar(List<BarData> bars, decimal wickBodyRatio = 2m)
    {
        var output = new List<Detection>();
        if (bars == null || bars.Count < 1) return output;
        for (int i = 0; i < bars.Count; i++)
        {
            var b = bars[i];
            var body = Body(b);
            var rng  = Range(b);
            if (body == 0m || rng == 0m) continue;
            var upper = UpperWick(b);
            var lower = LowerWick(b);

            // Hammer：下影線 ≥ ratio × 實體 且 上影線 ≤ 實體
            if (lower >= wickBodyRatio * body && upper <= body)
            {
                var conf = Math.Min(0.95m, 0.55m + lower / rng * 0.4m);
                output.Add(new Detection
                {
                    Type = "Hammer", Direction = Direction.Bullish,
                    BarIndex = i, Price = b.Close, Confidence = Math.Round(conf, 3),
                    Reason = $"下影線 {lower:F2} ≥ {wickBodyRatio}× 實體 {body:F2}（測試低點被買盤撐住）",
                });
            }
            // Shooting Star：上影線 ≥ ratio × 實體 且 下影線 ≤ 實體
            else if (upper >= wickBodyRatio * body && lower <= body)
            {
                var conf = Math.Min(0.95m, 0.55m + upper / rng * 0.4m);
                output.Add(new Detection
                {
                    Type = "Shooting_Star", Direction = Direction.Bearish,
                    BarIndex = i, Price = b.Close, Confidence = Math.Round(conf, 3),
                    Reason = $"上影線 {upper:F2} ≥ {wickBodyRatio}× 實體 {body:F2}（測試高點被賣盤打回）",
                });
            }
        }
        return output;
    }

    public static List<Detection> DetectInsideBar(List<BarData> bars)
    {
        var output = new List<Detection>();
        if (bars == null || bars.Count < 2) return output;
        for (int i = 1; i < bars.Count; i++)
        {
            var prev = bars[i - 1];
            var cur  = bars[i];
            if (cur.High < prev.High && cur.Low > prev.Low)
            {
                output.Add(new Detection
                {
                    Type = "Inside_Bar", Direction = Direction.Neutral,
                    BarIndex = i, Price = cur.Close, Confidence = 0.55m,
                    Reason = $"被前一根包住：[{cur.Low:F2},{cur.High:F2}] ⊂ [{prev.Low:F2},{prev.High:F2}]（盤整等突破）",
                });
            }
        }
        return output;
    }

    public static List<Detection> DetectOutsideBar(List<BarData> bars)
    {
        var output = new List<Detection>();
        if (bars == null || bars.Count < 2) return output;
        for (int i = 1; i < bars.Count; i++)
        {
            var prev = bars[i - 1];
            var cur  = bars[i];
            if (cur.High > prev.High && cur.Low < prev.Low)
            {
                var dirIsBull = cur.Close > cur.Open;
                var dir = dirIsBull ? Direction.Bullish : Direction.Bearish;
                var typeName = dirIsBull ? "Bullish_Outside_Bar" : "Bearish_Outside_Bar";
                output.Add(new Detection
                {
                    Type = typeName, Direction = dir,
                    BarIndex = i, Price = cur.Close, Confidence = 0.65m,
                    Reason = $"吞噬前一根範圍：[{cur.Low:F2},{cur.High:F2}] ⊃ [{prev.Low:F2},{prev.High:F2}]",
                });
            }
        }
        return output;
    }

    public static List<Detection> DetectStar(List<BarData> bars)
    {
        var output = new List<Detection>();
        if (bars == null || bars.Count < 3) return output;
        for (int i = 2; i < bars.Count; i++)
        {
            var b1 = bars[i - 2];
            var b2 = bars[i - 1];
            var b3 = bars[i];
            var body1 = Body(b1);
            var body2 = Body(b2);
            var body3 = Body(b3);
            if (body1 == 0m || body3 == 0m) continue;

            // 中間 K 必須小（< 30% of 第 1 根）
            if (body2 > body1 * 0.3m) continue;

            var bar1Bull = b1.Close > b1.Open;
            var bar1Bear = b1.Close < b1.Open;
            var bar3Bull = b3.Close > b3.Open;
            var bar3Bear = b3.Close < b3.Open;
            var mid1 = (b1.Open + b1.Close) / 2m;

            // Morning Star: bar1 bear, bar3 bull closing above bar1 mid, body3 ≥ 50% body1
            if (bar1Bear && bar3Bull && b3.Close > mid1 && body3 >= body1 * 0.5m)
            {
                var conf = Math.Min(0.95m, 0.65m + body3 / body1 * 0.2m);
                output.Add(new Detection
                {
                    Type = "Morning_Star", Direction = Direction.Bullish,
                    BarIndex = i, Price = b3.Close, Confidence = Math.Round(conf, 3),
                    Reason = $"晨星反轉：跌→小體→漲收上方 (body3/body1 = {body3 / body1:F2})",
                });
            }
            else if (bar1Bull && bar3Bear && b3.Close < mid1 && body3 >= body1 * 0.5m)
            {
                var conf = Math.Min(0.95m, 0.65m + body3 / body1 * 0.2m);
                output.Add(new Detection
                {
                    Type = "Evening_Star", Direction = Direction.Bearish,
                    BarIndex = i, Price = b3.Close, Confidence = Math.Round(conf, 3),
                    Reason = $"夜星反轉：漲→小體→跌收下方 (body3/body1 = {body3 / body1:F2})",
                });
            }
        }
        return output;
    }

    public static List<Detection> DetectDoji(List<BarData> bars, decimal bodyRangeRatio = 0.10m)
    {
        var output = new List<Detection>();
        if (bars == null || bars.Count < 1) return output;
        for (int i = 0; i < bars.Count; i++)
        {
            var b = bars[i];
            var rng = Range(b);
            if (rng == 0m) continue;
            var body = Body(b);
            if (body / rng < bodyRangeRatio)
            {
                output.Add(new Detection
                {
                    Type = "Doji", Direction = Direction.Neutral,
                    BarIndex = i, Price = b.Close, Confidence = 0.50m,
                    Reason = $"實體 {body:F2} / 範圍 {rng:F2} = {body / rng:P} （猶豫訊號）",
                });
            }
        }
        return output;
    }

    /// <summary>
    /// 跑全部 6 種 PA 偵測、回最近 maxAgeBars 根內出現的訊號（時間由新到舊）。
    /// </summary>
    public static List<Detection> DetectAll(List<BarData> bars, int maxAgeBars = 30)
    {
        if (bars == null || bars.Count < 3) return new List<Detection>();
        var cutoff = bars.Count - 1 - maxAgeBars;
        var all = new List<Detection>();
        all.AddRange(DetectEngulfing(bars));
        all.AddRange(DetectPinBar(bars));
        all.AddRange(DetectInsideBar(bars));
        all.AddRange(DetectOutsideBar(bars));
        all.AddRange(DetectStar(bars));
        all.AddRange(DetectDoji(bars));
        return all
            .Where(d => d.BarIndex >= cutoff)
            .OrderByDescending(d => d.BarIndex)
            .ToList();
    }
}
