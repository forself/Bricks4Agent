using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// SuperTrend 策略——ATR 動態趨勢線跟隨。
///
/// 訊號規則：
///   trend == +1（多頭）+ dist > 1% → buy（確認在趨勢線上、有空間）
///   trend == -1（空頭）+ dist > 1% → sell
///   dist < 0.5% → hold（接近反轉、不追進）
///
/// 信心度線性映射：
///   buy/sell confidence = clamp(0.5 + dist% / 5, 0.5, 0.95)
///   例：dist=2% → 0.9；dist=1% → 0.7；dist=0.5% → 0.6
///
/// 設計對標：朋友 ai-quant-starter2/app/services/strategy_selector.py:s_supertrend。
/// </summary>
public class SuperTrendStrategy : IStrategy
{
    public string Name => "super_trend";
    public string Description => "SuperTrend — ATR 動態趨勢線：順勢進場、線扮演移動停損";
    public StrategyCategory Category => StrategyCategory.Trend;
    public int MinBars => 12;
    public decimal MinCapitalUsdt => 100m;

    private const int AtrPeriod = 10;
    private const decimal Multiplier = 3m;
    private const decimal NearReversalPct = 0.5m;
    private const decimal MinConfirmPct = 1m;

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["atr_period"] = new() { Type = "int",     Default = 10, Min = 7,  Max = 21, Step = 1, Description = "ATR 週期" },
        ["multiplier"] = new() { Type = "decimal", Default = 3,  Min = 2,  Max = 5,  Step = 0.5, Description = "SuperTrend 倍數" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        var st = SuperTrend.Compute(bars, AtrPeriod, Multiplier);
        if (st == null) return Hold(config, "Not enough data for SuperTrend");

        var price = bars[^1].Close;
        string action = "hold";
        decimal confidence = 0.5m;
        string reason;

        if (st.DistancePct < NearReversalPct)
        {
            reason = $"SuperTrend {st.Value} 距離 {st.DistancePct:F2}% — 接近反轉、不追";
        }
        else if (st.Trend == 1 && st.DistancePct >= MinConfirmPct)
        {
            action = "buy";
            confidence = Math.Clamp(0.5m + st.DistancePct / 5m, 0.5m, 0.95m);
            reason = $"SuperTrend 多頭、價 {price} > 停損線 {st.Value}（距離 {st.DistancePct:F2}%）";
        }
        else if (st.Trend == -1 && st.DistancePct >= MinConfirmPct)
        {
            action = "sell";
            confidence = Math.Clamp(0.5m + st.DistancePct / 5m, 0.5m, 0.95m);
            reason = $"SuperTrend 空頭、價 {price} < 停損線 {st.Value}（距離 {st.DistancePct:F2}%）";
        }
        else
        {
            reason = $"SuperTrend 方向 {(st.Trend == 1 ? "多" : "空")} 但距離 {st.DistancePct:F2}% 不足 — 觀望";
        }

        return new Signal
        {
            SignalId   = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy   = Name,
            Symbol     = config.Symbol,
            Exchange   = config.Exchange,
            Action     = action,
            Confidence = Math.Round(confidence, 2),
            Reason     = reason,
            Interval   = config.Interval,
            Indicators = new()
            {
                ["price"] = Math.Round(price, 4),
                ["supertrend"] = st.Value,
                ["trend"] = st.Trend,
                ["distance_pct"] = st.DistancePct,
            },
        };
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = "super_trend",
        Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
