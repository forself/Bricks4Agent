using StrategyWorker.Models;

namespace StrategyWorker.Engine.Indicators;

/// <summary>
/// 斐波那契回撤水平計算（純數學工具，不持狀態）。
///
/// 從擺動 (swing) 的最高點到最低點畫出 Fibonacci 回撤：
///
///    High ────────────────────────  (100%)
///         ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─   0.786
///         ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─   0.618  ← 黃金分割
///         ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─   0.500
///         ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─   0.382
///         ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─   0.236
///    Low  ────────────────────────  (0%)
///
/// 應用：上升趨勢中，價格回落到 0.382–0.618 區間並反彈 → 買進訊號。
///      下降趨勢中，價格反彈到 0.382–0.618 區間並拒絕 → 賣出訊號。
/// </summary>
public static class FibonacciLevels
{
    // 標準 Fibonacci 回撤比率
    public static readonly decimal[] RetracementRatios = { 0.236m, 0.382m, 0.500m, 0.618m, 0.786m };

    // 延伸比率（突破擺動後的目標）
    public static readonly decimal[] ExtensionRatios = { 1.272m, 1.618m, 2.000m };

    /// <summary>
    /// 從最近 lookback 根 K 線找擺動高低點。
    /// </summary>
    public static (decimal High, int HighIndex, decimal Low, int LowIndex) FindSwing(
        List<BarData> bars, int lookback)
    {
        if (bars.Count == 0) return (0m, -1, 0m, -1);
        var start = Math.Max(0, bars.Count - lookback);

        decimal high = decimal.MinValue;
        decimal low  = decimal.MaxValue;
        int hi = start, li = start;
        for (int i = start; i < bars.Count; i++)
        {
            if (bars[i].High > high) { high = bars[i].High; hi = i; }
            if (bars[i].Low  < low)  { low  = bars[i].Low;  li = i; }
        }
        return (high, hi, low, li);
    }

    /// <summary>
    /// 依指定方向算出每個 Fib 比率對應的價格。
    /// direction = "up"：高點在後，回撤往低點方向（適合多頭進場）
    /// direction = "down"：低點在後，反彈往高點方向（適合空頭進場）
    /// </summary>
    public static Dictionary<decimal, decimal> Levels(decimal high, decimal low, string direction)
    {
        var range = high - low;
        var map = new Dictionary<decimal, decimal>();

        if (direction == "up")
        {
            // 從 high 向下回撤：level_price = high - ratio * range
            foreach (var r in RetracementRatios) map[r] = Math.Round(high - r * range, 4);
        }
        else
        {
            // 從 low 向上反彈：level_price = low + ratio * range
            foreach (var r in RetracementRatios) map[r] = Math.Round(low + r * range, 4);
        }
        return map;
    }

    /// <summary>
    /// 依方向算出每個擴展比率對應的「目標價」(突破擺動後的延伸目標、當停利用)。
    /// direction = "up"：目標在 high 之上 → low + ratio*range（ratio≥1 故必在 high 上方）
    /// direction = "down"：目標在 low 之下 → high - ratio*range
    /// </summary>
    public static Dictionary<decimal, decimal> ExtensionLevels(decimal high, decimal low, string direction)
    {
        var range = high - low;
        var map = new Dictionary<decimal, decimal>();
        if (direction == "up")
            foreach (var r in ExtensionRatios) map[r] = Math.Round(low + r * range, 4);
        else
            foreach (var r in ExtensionRatios) map[r] = Math.Round(high - r * range, 4);
        return map;
    }

    /// <summary>
    /// 回傳目前價格的「回撤比例」(0-1)。
    /// 0 = 完全在 low；1 = 完全在 high；0.5 = 正好在擺動中點。
    /// </summary>
    public static decimal RetracementRatio(decimal currentPrice, decimal high, decimal low)
    {
        var range = high - low;
        if (range <= 0) return 0m;
        var ratio = (currentPrice - low) / range;
        return Math.Round(Math.Clamp(ratio, 0m, 1m), 4);
    }

    /// <summary>
    /// 檢查某個價格是否落在「黃金區」(0.382 - 0.618)。
    /// 黃金區是 Fibonacci 交易者公認最常反轉的區域。
    /// </summary>
    public static bool IsInGoldenZone(decimal currentPrice, decimal high, decimal low, string direction)
    {
        var ratio = RetracementRatio(currentPrice, high, low);
        // direction "up"：回撤到 0.382–0.618 = 反向 ratio 0.382–0.618 from top
        //   => currentPrice 的 ratio 應該在 (1 - 0.618 = 0.382) 到 (1 - 0.382 = 0.618) 之間
        // 所以上漲與下跌其實是同一區間 ratio ∈ [0.382, 0.618]
        return ratio >= 0.382m && ratio <= 0.618m;
    }
}
