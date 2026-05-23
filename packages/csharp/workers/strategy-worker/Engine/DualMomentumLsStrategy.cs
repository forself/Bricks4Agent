using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 雙時框動量共振(Dual Momentum,多空)—— 短、長兩個 ROC 同號才出手,過濾單一時框的雜訊:
///   ROC(short) > 0 且 ROC(long) > 0 → buy(雙多)
///   ROC(short) < 0 且 ROC(long) < 0 → sell(雙空)
///   方向不一致 → hold(避開轉折/盤整的假訊號)
/// 是「組合型」動量(兩條訊號的 AND),比單時框穩;多空原生。無 lookahead:ROC 純回看、已測。
/// </summary>
public class DualMomentumLsStrategy : IStrategy
{
    public string Name => "dual_mom_ls";
    public string Description => "Dual Momentum(多空)— 短/長 ROC 同號才進、雜訊過濾";
    public StrategyCategory Category => StrategyCategory.Momentum;
    public int MinBars => 70;
    public decimal MinCapitalUsdt => 100m;

    private const int ShortRoc = 20;
    private const int LongRoc = 60;

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["dm_short"] = new() { Type = "int", Default = ShortRoc, Min = 10, Max = 40,  Step = 5,  Description = "短期 ROC 週期" },
        ["dm_long"]  = new() { Type = "int", Default = LongRoc,  Min = 40, Max = 120, Step = 10, Description = "長期 ROC 週期" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        int sP = config.GetParam("dm_short", ShortRoc);
        int lP = config.GetParam("dm_long", LongRoc);
        if (bars.Count < lP + 1) return Hold(config, $"資料不足(需 {lP + 1}+ 根)");

        var sRoc = Roc.Compute(bars, sP);
        var lRoc = Roc.Compute(bars, lP);
        if (sRoc == null || lRoc == null) return Hold(config, "ROC 無法計算");

        string action; decimal confidence; string reason;
        if (sRoc > 0m && lRoc > 0m)
        {
            action = "buy";
            confidence = Math.Clamp(0.6m + (sRoc.Value + lRoc.Value) / 100m, 0.6m, 0.95m);
            reason = $"雙多:ROC{sP}={sRoc:F1}%、ROC{lP}={lRoc:F1}% 同為正 — 做多";
        }
        else if (sRoc < 0m && lRoc < 0m)
        {
            action = "sell";
            confidence = Math.Clamp(0.6m + (-sRoc.Value - lRoc.Value) / 100m, 0.6m, 0.95m);
            reason = $"雙空:ROC{sP}={sRoc:F1}%、ROC{lP}={lRoc:F1}% 同為負 — 做空";
        }
        else
        {
            action = "hold";
            confidence = 0.5m;
            reason = $"方向分歧:ROC{sP}={sRoc:F1}%、ROC{lP}={lRoc:F1}% — 維持";
        }

        return new Signal
        {
            SignalId = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
            Action = action, Confidence = Math.Round(confidence, 2), Reason = reason,
            Interval = config.Interval,
            Indicators = new() { ["roc_short"] = sRoc.Value, ["roc_long"] = lRoc.Value, ["price"] = Math.Round(bars[^1].Close, 4) },
        };
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16], Strategy = "dual_mom_ls",
        Symbol = c.Symbol, Exchange = c.Exchange, Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
