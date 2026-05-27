namespace StrategyWorker.Engine;

/// <summary>
/// Drawdown-aware position sizer(2026-05-27 Q1.5、Roadmap Q1.5)。
///
/// 核心想法:**回撤期不該開全倉、要按 DD 比例縮小**。
/// 跟「DD 8% 完全停止」(現有 CB)互補:
///   - CB:DD 達上限完全停(binary、保命)
///   - DD-aware:DD 從 0 到上限 gradient 縮(continuous、平滑降風險)
///
/// 跟 Kelly / vol-target / mean-variance 互補:
///   - Kelly:策略獨立 sizing
///   - Vol-target:時間維度縮放(現在 vol 多大)
///   - Mean-variance / ERC:策略間關係配重
///   - **DD-aware:結合 path dependence**(已虧多少、要怎麼撐回來)← 這支
///
/// 三種公式(從輕到嚴):
///   1. Linear:scalar = 1 - DD/max_DD(DD=0 全倉、DD=max 完全停)
///   2. Polynomial(Carver Ch 8 偏好):scalar = (1 - DD/max_DD)^2
///      - DD 小範圍緩慢降、DD 大範圍快速降、stop loss 性質強
///   3. Step:thresholds 分階(0-5%全倉、5-10% 75%、10-15% 50%、>15% 25%)
///      - 業界 prop firm 常用、心理層面好觀察
///
/// 業界教訓(Carver、Buffett):
///   - DD 是 path dependent risk、樣本 Sharpe 不反映
///   - 沒 DD-aware 等於賭「未來 DD 跟 backtest 一樣」(危險)
///   - DD-aware 換取「DD 平滑、心理可承受」(real-world critical)
/// </summary>
public static class DrawdownAwareSizer
{
    /// <summary>
    /// Linear scale:scalar = 1 - DD/max_DD,clamp [0, 1]
    /// 簡單、業界常用入門版。
    /// </summary>
    public static decimal LinearScale(decimal currentDd, decimal maxDd)
    {
        if (maxDd <= 0m) return 1m;
        if (currentDd <= 0m) return 1m;
        decimal scalar = 1m - currentDd / maxDd;
        return Math.Clamp(scalar, 0m, 1m);
    }

    /// <summary>
    /// Polynomial scale(Carver Ch 8 偏好):scalar = (1 - DD/max_DD)^power
    /// 預設 power=2:DD 小範圍緩慢降、大範圍快速降。
    /// </summary>
    public static decimal PolynomialScale(decimal currentDd, decimal maxDd, decimal power = 2m)
    {
        if (maxDd <= 0m) return 1m;
        if (currentDd <= 0m) return 1m;
        decimal frac = 1m - currentDd / maxDd;
        if (frac <= 0m) return 0m;
        return (decimal)Math.Pow((double)frac, (double)power);
    }

    /// <summary>
    /// Step scale:離散 threshold 分階(業界 prop firm 風格)
    /// tier[i] = (ddThreshold, scalar)、按升序排列、currentDd 落哪個 tier 取對應 scalar。
    /// 預設 4 階:0-5% 全倉、5-10% 75%、10-15% 50%、>15% 25%
    /// </summary>
    public static decimal StepScale(decimal currentDd, (decimal Threshold, decimal Scalar)[]? tiers = null)
    {
        tiers ??= new[]
        {
            (Threshold: 0.05m, Scalar: 1.00m),
            (Threshold: 0.10m, Scalar: 0.75m),
            (Threshold: 0.15m, Scalar: 0.50m),
            (Threshold: 0.20m, Scalar: 0.25m),
        };
        if (currentDd <= 0m) return 1m;
        foreach (var (thr, sc) in tiers)
            if (currentDd <= thr) return sc;
        return 0m;   // 超過所有 threshold → 完全停
    }

    /// <summary>
    /// 從 equity curve 計算當前 DD%(從歷史 peak)
    /// 給 backtest simulation / runtime 用。
    /// </summary>
    public static decimal CurrentDdFromEquityCurve(IList<decimal> equityCurve)
    {
        if (equityCurve == null || equityCurve.Count < 2) return 0m;
        decimal peak = equityCurve[0];
        decimal current = equityCurve[^1];
        foreach (var v in equityCurve) if (v > peak) peak = v;
        if (peak <= 0m) return 0m;
        return Math.Max(0m, (peak - current) / peak);
    }

    /// <summary>
    /// Backtest simulation:對 equity curve 套 DD-aware sizing、看 final equity 跟 max DD 變化
    /// scaleMethod = "linear" / "poly" / "step"
    /// 回傳 (originalFinal, originalMaxDd, adjustedFinal, adjustedMaxDd)
    /// </summary>
    public static (decimal OrigFinal, decimal OrigMaxDd, decimal AdjFinal, decimal AdjMaxDd) Simulate(
        IList<decimal> originalEquityCurve,
        decimal maxAcceptableDd = 0.20m,
        string scaleMethod = "poly")
    {
        if (originalEquityCurve == null || originalEquityCurve.Count < 2) return (0, 0, 0, 0);

        decimal origFinal = originalEquityCurve[^1];
        decimal origMaxDd = 0m;
        decimal peak = originalEquityCurve[0];
        foreach (var v in originalEquityCurve)
        {
            if (v > peak) peak = v;
            if (peak > 0m)
            {
                decimal dd = (peak - v) / peak;
                if (dd > origMaxDd) origMaxDd = dd;
            }
        }

        // 模擬:每根 bar、按當下 DD 套 scalar、新 returns × scalar
        decimal adj = originalEquityCurve[0];
        decimal adjPeak = adj;
        decimal adjMaxDd = 0m;
        for (int i = 1; i < originalEquityCurve.Count; i++)
        {
            // 上一根 bar 的 DD(從上一根算的 peak)決定本根 sizing
            decimal prevPeak = peak;   // 已計算過、但這裡需要逐步重算
            // 重算 prev DD(用 original equity 算 peak、簡化)
            decimal oPeak = originalEquityCurve[0];
            for (int j = 0; j <= i - 1; j++) if (originalEquityCurve[j] > oPeak) oPeak = originalEquityCurve[j];
            decimal currentDd = oPeak > 0m ? Math.Max(0m, (oPeak - originalEquityCurve[i - 1]) / oPeak) : 0m;
            decimal scalar = scaleMethod switch
            {
                "linear" => LinearScale(currentDd, maxAcceptableDd),
                "step"   => StepScale(currentDd),
                _        => PolynomialScale(currentDd, maxAcceptableDd),
            };

            // 用 original return 但 × scalar
            decimal origRet = originalEquityCurve[i - 1] > 0m
                ? (originalEquityCurve[i] - originalEquityCurve[i - 1]) / originalEquityCurve[i - 1]
                : 0m;
            decimal adjRet = origRet * scalar;
            adj *= (1m + adjRet);

            if (adj > adjPeak) adjPeak = adj;
            if (adjPeak > 0m)
            {
                decimal d = (adjPeak - adj) / adjPeak;
                if (d > adjMaxDd) adjMaxDd = d;
            }
        }

        return (origFinal, origMaxDd, adj, adjMaxDd);
    }
}
