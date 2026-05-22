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

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["mfi_period"]     = new() { Type = "int",     Default = 14, Min = 7,  Max = 28, Step = 7, Description = "MFI 週期" },
        ["mfi_oversold"]   = new() { Type = "decimal", Default = 20, Min = 10, Max = 35, Step = 5, Description = "超賣門檻(買);弱訊號 +15" },
        ["mfi_overbought"] = new() { Type = "decimal", Default = 80, Min = 65, Max = 90, Step = 5, Description = "超買門檻(賣);弱訊號 -15" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        int period         = config.GetParam("mfi_period", 14);
        decimal oversold   = config.GetParam("mfi_oversold", 20m);
        decimal overbought = config.GetParam("mfi_overbought", 80m);
        decimal weakLow    = oversold + 15m;
        decimal weakHigh   = overbought - 15m;

        var mfi = Mfi.Compute(bars, period);
        if (mfi == null) return Hold(config, "Not enough data for MFI");

        string action = "hold"; decimal conf = 0.5m; string reason;
        var v = mfi.Value;

        if (v < oversold)        { action = "buy";  conf = 0.7m;  reason = $"MFI={v:F1} 資金超賣"; }
        else if (v < weakLow)    { action = "buy";  conf = 0.55m; reason = $"MFI={v:F1} 資金偏低"; }
        else if (v > overbought) { action = "sell"; conf = 0.7m;  reason = $"MFI={v:F1} 資金超買"; }
        else if (v > weakHigh)   { action = "sell"; conf = 0.55m; reason = $"MFI={v:F1} 資金偏高"; }
        else                                                       reason = $"MFI={v:F1} 中性";

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
