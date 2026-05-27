namespace StrategyWorker.Engine;

/// <summary>
/// Kelly fraction position sizing helper(2026-05-27 Q1 起步、Roadmap Q1.1)。
///
/// 用法:
///   var f = KellyPositionSizer.Compute(winRate: 0.55m, avgWin: 8.2m, avgLoss: 5.1m);
///   var safe = KellyPositionSizer.FractionalKelly(f, fraction: 0.25m);  // quarter-Kelly
///   var pct = KellyPositionSizer.ClampPct(safe, min: 0m, max: 0.20m);   // 最高 20% 配重
///
/// 心法(Robert Carver《Systematic Trading》Ch 5):
///   - 完整 Kelly 數學最優、但 path dependence 慘(中途 DD 嚇人)
///   - 業界普遍用 quarter-Kelly(f* / 4)、保留增長 + 大降 DD
///   - half-Kelly 略激進、quarter-Kelly 是 sweet spot
///   - 任何單一策略 max 20-30% 防 over-concentration
///
/// 公式:f* = (b * p - q) / b
///   - p = win probability
///   - q = 1 - p = loss probability
///   - b = avg_win / avg_loss(R-multiple)
///   - f* ≥ 0:正期望值、優化下注比例
///   - f* < 0:負期望值、根本不該下注
///
/// 應用:現有 LongShortBacktestEngine 用 notionalPct = 0.95 固定 sizing,
/// 之後改成「Kelly-implied per-strategy notional」、每月用最近 90 天 t-stat 更新。
/// </summary>
public static class KellyPositionSizer
{
    /// <summary>
    /// 從 win rate / avg win / avg loss 計算 raw Kelly f*。
    /// avgWin / avgLoss 用「相對 entry 的報酬 %」(都正數、loss 不帶負號)。
    /// 回 0 = 負期望、不該下注。
    /// </summary>
    public static decimal Compute(decimal winRate, decimal avgWin, decimal avgLoss)
    {
        if (winRate <= 0m || winRate >= 1m) return 0m;
        if (avgWin <= 0m || avgLoss <= 0m) return 0m;

        decimal b = avgWin / avgLoss;
        decimal p = winRate;
        decimal q = 1m - p;

        decimal kelly = (b * p - q) / b;
        return Math.Max(0m, kelly);
    }

    /// <summary>
    /// Fractional Kelly(quarter-Kelly = 0.25、half-Kelly = 0.5)。
    /// 業界預設 quarter-Kelly(最佳 DD 控制 vs 增長)。
    /// </summary>
    public static decimal FractionalKelly(decimal fullKelly, decimal fraction = 0.25m)
    {
        if (fraction <= 0m) return 0m;
        return fullKelly * fraction;
    }

    /// <summary>
    /// 配重百分比 clamp(per-strategy max 20% 防 over-concentration)。
    /// 回傳 [0, max] 之間的值。
    /// </summary>
    public static decimal ClampPct(decimal pct, decimal min = 0m, decimal max = 0.20m)
    {
        return Math.Clamp(pct, min, max);
    }

    /// <summary>
    /// 一鍵 helper:從 backtest stats → 安全 production sizing 比例。
    /// 預設 quarter-Kelly + max 20% clamp。
    /// </summary>
    public static decimal RecommendedPct(
        decimal winRate, decimal avgWin, decimal avgLoss,
        decimal fraction = 0.25m, decimal maxPct = 0.20m)
    {
        var raw = Compute(winRate, avgWin, avgLoss);
        var frac = FractionalKelly(raw, fraction);
        return ClampPct(frac, 0m, maxPct);
    }

    /// <summary>
    /// 文字解讀(diagnostic 用、寫在報告裡)。
    /// </summary>
    public static string Explain(decimal winRate, decimal avgWin, decimal avgLoss, decimal fraction = 0.25m, decimal maxPct = 0.20m)
    {
        if (winRate <= 0m || winRate >= 1m) return "Win rate 無效(需 0 < p < 1)";
        if (avgWin <= 0m || avgLoss <= 0m) return "Avg win / loss 需正值";

        var b = avgWin / avgLoss;
        var full = Compute(winRate, avgWin, avgLoss);
        var frac = FractionalKelly(full, fraction);
        var safe = ClampPct(frac, 0m, maxPct);

        if (full <= 0m)
            return $"❌ 負期望值(b={b:F2}, p={winRate:P0})、不該下注、推薦 0%";

        if (safe < frac)
            return $"⚠ 被 {maxPct:P0} max cap 限制(raw {(int)(full * 100)}% → {fraction:P0}-Kelly {frac * 100:F1}% → cap {safe * 100:F1}%)";

        return $"✅ 推薦 {safe * 100:F1}% notional({fraction:P0}-Kelly、raw f*={full * 100:F1}%、b={b:F2}、p={winRate:P0})";
    }
}
