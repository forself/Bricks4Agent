using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// Parabolic SAR 趨勢跟隨策略。
///
/// 訊號規則：
///   bullish + 距離 ≥ 1% → buy
///   bearish + 距離 ≥ 1% → sell
///   距離 &lt; 1% → hold（接近反轉、不進場、等翻轉）
///
/// 設計對標：朋友 ai-quant-starter2/strategy_selector.py:s_parabolic_sar。
/// </summary>
public class ParabolicSarStrategy : IStrategy
{
    public string Name => "parabolic_sar";
    public string Description => "Parabolic SAR — 加速因子拋物線停損、順勢進場";
    public StrategyCategory Category => StrategyCategory.Trend;
    public int MinBars => 30;
    public decimal MinCapitalUsdt => 100m;

    private const decimal MinDistancePct = 1m;
    private const decimal NearReversalPct = 0.3m;

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["psar_af_step"] = new() { Type = "decimal", Default = 0.02m, Min = 0.01m, Max = 0.04m, Step = 0.01m, Description = "加速因子步長(初始值同此)" },
        ["psar_max_af"]  = new() { Type = "decimal", Default = 0.2m,  Min = 0.1m,  Max = 0.4m,  Step = 0.1m,  Description = "加速因子上限" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        decimal afStep = config.GetParam("psar_af_step", 0.02m);
        decimal maxAf  = config.GetParam("psar_max_af", 0.2m);

        var s = ParabolicSar.Compute(bars, afStep, afStep, maxAf);
        if (s == null) return Hold(config, "Not enough data for Parabolic SAR");

        var price = bars[^1].Close;
        string action = "hold"; decimal conf = 0.5m; string reason;

        if (s.DistancePct < NearReversalPct)
        {
            reason = $"SAR {s.Sar} 距離 {s.DistancePct:F2}% — 接近反轉、不追";
        }
        else if (s.IsBullish && s.DistancePct >= MinDistancePct)
        {
            action = "buy";
            conf = Math.Clamp(0.5m + s.DistancePct / 10m, 0.5m, 0.85m);
            reason = $"SAR 多頭、現價 {price} &gt; SAR {s.Sar}（距 {s.DistancePct:F2}%）";
        }
        else if (!s.IsBullish && s.DistancePct >= MinDistancePct)
        {
            action = "sell";
            conf = Math.Clamp(0.5m + s.DistancePct / 10m, 0.5m, 0.85m);
            reason = $"SAR 空頭、現價 {price} &lt; SAR {s.Sar}（距 {s.DistancePct:F2}%）";
        }
        else
        {
            reason = $"SAR {(s.IsBullish ? "多" : "空")}、距離 {s.DistancePct:F2}% 不夠 — 觀望";
        }

        return new Signal
        {
            SignalId = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
            Action = action, Confidence = Math.Round(conf, 2), Reason = reason, Interval = config.Interval,
            Indicators = new()
            {
                ["price"] = Math.Round(price, 4),
                ["sar"] = s.Sar,
                ["is_bullish"] = s.IsBullish ? 1m : 0m,
                ["distance_pct"] = s.DistancePct,
            },
        };
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = "parabolic_sar", Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
