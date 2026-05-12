using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// Chaikin Money Flow (CMF) 資金流向策略。
///
/// 訊號規則：
///   CMF &gt; +0.1 → buy strong（強力買盤、conf 0.7）
///   CMF &gt; 0    → buy weak（溫和買盤、conf 0.55）
///   CMF &lt; -0.1 → sell strong（強力賣盤、conf 0.7）
///   CMF &lt; 0    → sell weak
///   ±0.05 內視為中性、不出訊號
///
/// 設計對標：朋友 ai-quant-starter2/strategy_selector.py:s_chaikin_mf。
/// </summary>
public class ChaikinMfStrategy : IStrategy
{
    public string Name => "chaikin_mf";
    public string Description => "Chaikin Money Flow — K 棒位置 × 成交量、判資金真實流向";
    public StrategyCategory Category => StrategyCategory.Volume;
    public int MinBars => 25;
    public decimal MinCapitalUsdt => 50m;

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        var cmf = ChaikinMf.Compute(bars);
        if (cmf == null) return Hold(config, "Not enough data for Chaikin MF");

        string action = "hold"; decimal conf = 0.5m; string reason;
        var v = cmf.Value;

        if (v > 0.1m)        { action = "buy";  conf = 0.7m;  reason = $"CMF={v:F3} 強力買盤"; }
        else if (v > 0.05m)  { action = "buy";  conf = 0.55m; reason = $"CMF={v:F3} 溫和買盤"; }
        else if (v < -0.1m)  { action = "sell"; conf = 0.7m;  reason = $"CMF={v:F3} 強力賣盤"; }
        else if (v < -0.05m) { action = "sell"; conf = 0.55m; reason = $"CMF={v:F3} 溫和賣盤"; }
        else                                                   reason = $"CMF={v:F3} 中性";

        return new Signal
        {
            SignalId = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
            Action = action, Confidence = Math.Round(conf, 2), Reason = reason, Interval = config.Interval,
            Indicators = new() { ["cmf"] = v, ["price"] = Math.Round(bars[^1].Close, 4) },
        };
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = "chaikin_mf", Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
