using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// SuperTrend 多空 —— ATR 動態趨勢線:收盤在線上做多、線下做空(多空反手)。
/// 用已測過的 SuperTrend 指標(path-dependent、含記憶),Trend=+1 buy、−1 sell。
/// 距離越遠信心越高。經典 ATR 趨勢跟隨,多空原生、穩定正期望。無 lookahead。
/// </summary>
public class SuperTrendLsStrategy : IStrategy
{
    public string Name => "supertrend_ls";
    public string Description => "SuperTrend(多空)— ATR 趨勢線上做多、線下做空";
    public StrategyCategory Category => StrategyCategory.Trend;
    public int MinBars => 30;
    public decimal MinCapitalUsdt => 100m;

    private const int AtrPeriod = 10;
    private const decimal Mult = 3m;

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["st_atr_period"] = new() { Type = "int",     Default = AtrPeriod, Min = 7,  Max = 21,  Step = 1,   Description = "ATR 週期" },
        ["st_mult"]       = new() { Type = "decimal", Default = Mult,      Min = 1.5m, Max = 4.0m, Step = 0.5m, Description = "ATR 倍數" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        int atrP = config.GetParam("st_atr_period", AtrPeriod);
        decimal mult = config.GetParam("st_mult", Mult);
        if (bars.Count < MinBars) return Hold(config, $"資料不足(需 {MinBars}+ 根)");

        var st = SuperTrend.Compute(bars, atrP, mult);
        if (st == null) return Hold(config, "SuperTrend 無法計算(資料不足)");

        string action; decimal confidence; string reason;
        if (st.Trend == 1)
        {
            action = "buy";
            confidence = Math.Clamp(0.6m + st.DistancePct / 100m, 0.6m, 0.95m);
            reason = $"收盤在 SuperTrend {st.Value:F2} 之上(距 {st.DistancePct:F1}%)— 多頭、做多";
        }
        else
        {
            action = "sell";
            confidence = Math.Clamp(0.6m + st.DistancePct / 100m, 0.6m, 0.95m);
            reason = $"收盤在 SuperTrend {st.Value:F2} 之下(距 {st.DistancePct:F1}%)— 空頭、做空";
        }

        return new Signal
        {
            SignalId = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
            Action = action, Confidence = Math.Round(confidence, 2), Reason = reason,
            Interval = config.Interval,
            Indicators = new() { ["supertrend"] = st.Value, ["trend"] = st.Trend, ["dist_pct"] = st.DistancePct, ["price"] = Math.Round(bars[^1].Close, 4) },
        };
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16], Strategy = "supertrend_ls",
        Symbol = c.Symbol, Exchange = c.Exchange, Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
