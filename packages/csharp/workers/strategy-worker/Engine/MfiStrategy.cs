using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// MFI（Money Flow Index）資金面均值回歸策略。
///
/// 訊號規則：
///   MFI &lt; 20 → buy（資金超賣、conf 0.7）
///   MFI &lt; 35 → buy weak（conf 0.55）
///   MFI &gt; 80 → sell（資金超買、conf 0.7）
///   MFI &gt; 65 → sell weak
///
/// 設計對標：朋友 ai-quant-starter2/strategy_selector.py:s_mfi。
/// </summary>
public class MfiStrategy : IStrategy
{
    public string Name => "mfi";
    public string Description => "MFI 資金流量指數 — 量價加權的 RSI、20/80 為強訊號區";
    public StrategyCategory Category => StrategyCategory.MeanReversion;
    public int MinBars => 16;
    public decimal MinCapitalUsdt => 50m;

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        var mfi = Mfi.Compute(bars);
        if (mfi == null) return Hold(config, "Not enough data for MFI");

        string action = "hold"; decimal conf = 0.5m; string reason;
        var v = mfi.Value;

        if (v < 20m)        { action = "buy";  conf = 0.7m;  reason = $"MFI={v:F1} 資金超賣"; }
        else if (v < 35m)   { action = "buy";  conf = 0.55m; reason = $"MFI={v:F1} 資金偏低"; }
        else if (v > 80m)   { action = "sell"; conf = 0.7m;  reason = $"MFI={v:F1} 資金超買"; }
        else if (v > 65m)   { action = "sell"; conf = 0.55m; reason = $"MFI={v:F1} 資金偏高"; }
        else                                                  reason = $"MFI={v:F1} 中性";

        return new Signal
        {
            SignalId = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
            Action = action, Confidence = Math.Round(conf, 2), Reason = reason, Interval = config.Interval,
            Indicators = new() { ["mfi"] = v, ["price"] = Math.Round(bars[^1].Close, 4) },
        };
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = "mfi", Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
