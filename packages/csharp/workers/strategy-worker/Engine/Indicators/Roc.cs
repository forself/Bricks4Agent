namespace StrategyWorker.Engine.Indicators;

/// <summary>
/// ROC (Rate of Change) — 簡單動能指標：
///   ROC[t] = (close[t] - close[t - n]) / close[t - n] × 100
///
/// 用途：
///   - 正值 = 期間內價格上漲、絕對值越大動能越強
///   - 0 線突破 = 短期趨勢翻轉訊號
///   - 跟 RSI 不同：ROC 不限制在 0-100、可放大顯示加速/減速
///
/// 預設 period=14（跟 RSI 同步、方便對照）。
///
/// 設計參考：朋友 ai-quant-starter2 的同名實作（only-look-back、無 lookahead）。
/// </summary>
public static class Roc
{
    public static decimal? Compute(List<BarData> bars, int period = 14)
    {
        if (bars == null || bars.Count < period + 1) return null;

        var current = bars[^1].Close;
        var past = bars[^(period + 1)].Close;
        if (past == 0m) return null;

        return Math.Round((current - past) / past * 100m, 4);
    }
}
