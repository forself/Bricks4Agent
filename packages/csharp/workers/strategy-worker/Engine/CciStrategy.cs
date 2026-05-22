using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// CCI（商品通道指數）均值回歸策略。
///
/// 訊號規則：
///   CCI &lt; -100 → buy（超賣、conf 0.7）
///   CCI &lt; -50  → buy weak（conf 0.55）
///   CCI &gt; +100 → sell（超買、conf 0.7）
///   CCI &gt; +50  → sell weak
///   |CCI| ≤ 50 → hold（中性區）
///
/// 設計對標：朋友 ai-quant-starter2/strategy_selector.py:s_cci。
/// </summary>
public class CciStrategy : IStrategy
{
    public string Name => "cci";
    public string Description => "CCI 商品通道指數 — 超買超賣均值回歸（±100 強訊號）";
    public StrategyCategory Category => StrategyCategory.MeanReversion;
    public int MinBars => 25;
    public decimal MinCapitalUsdt => 50m;

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["cci_period"]    = new() { Type = "int",     Default = 20,  Min = 10, Max = 40,  Step = 5,  Description = "CCI 週期" },
        ["cci_threshold"] = new() { Type = "decimal", Default = 100, Min = 80, Max = 150, Step = 10, Description = "強訊號門檻(±);弱訊號取一半" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        int period   = config.GetParam("cci_period", 20);
        decimal th   = config.GetParam("cci_threshold", 100m);
        decimal weak = th / 2m;

        var cci = Cci.Compute(bars, period);
        if (cci == null) return Hold(config, "Not enough data for CCI");

        string action = "hold"; decimal conf = 0.5m; string reason;
        var v = cci.Value;

        if (v < -th)        { action = "buy";  conf = 0.7m;  reason = $"CCI={v:F0} 超賣"; }
        else if (v < -weak) { action = "buy";  conf = 0.55m; reason = $"CCI={v:F0} 偏低"; }
        else if (v > th)    { action = "sell"; conf = 0.7m;  reason = $"CCI={v:F0} 超買"; }
        else if (v > weak)  { action = "sell"; conf = 0.55m; reason = $"CCI={v:F0} 偏高"; }
        else                                                   reason = $"CCI={v:F0} 中性";

        return new Signal
        {
            SignalId = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
            Action = action, Confidence = Math.Round(conf, 2), Reason = reason, Interval = config.Interval,
            Indicators = new() { ["cci"] = v, ["price"] = Math.Round(bars[^1].Close, 4) },
        };
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = "cci", Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
