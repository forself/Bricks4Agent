using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 趨勢對齊的 Z-score 均值回歸(多空)—— 純逆勢做空在加密牛市會被軋爆,所以加「大趨勢過濾」:
/// **只在上升趨勢買跌、只在下降趨勢空漲**,絕不逆大勢。
///   z = (close − SMA(n)) / std(n);trend = SMA(trendSma)
///   z ≤ −entryZ 且 close>trend(升勢回檔)→ buy
///   z ≥ +entryZ 且 close<trend(跌勢反彈)→ sell(做空)
///   其餘 hold
/// 進在「回檔/反彈」、與順勢趨勢家族的進場點錯開 → 去相關,且不會逆勢挨軋。無 lookahead。
/// </summary>
public class BollingerRevertLsStrategy : IStrategy
{
    public string Name => "bb_revert_ls";
    public string Description => "趨勢對齊 Z-score 回歸(多空)— 升勢買跌、跌勢空漲,不逆大勢";
    public StrategyCategory Category => StrategyCategory.MeanReversion;
    public int MinBars => 110;
    public decimal MinCapitalUsdt => 100m;

    private const int Period = 20;
    private const int TrendSma = 100;
    private const decimal EntryZ = 2.0m;

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["bb_period"]    = new() { Type = "int",     Default = Period,   Min = 10,   Max = 50,   Step = 5,   Description = "均值/標準差窗" },
        ["bb_trend_sma"] = new() { Type = "int",     Default = TrendSma, Min = 50,   Max = 200,  Step = 10,  Description = "大趨勢過濾均線" },
        ["bb_entry_z"]   = new() { Type = "decimal", Default = EntryZ,   Min = 1.0m, Max = 3.0m, Step = 0.25m, Description = "進場偏離(σ 倍數)" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        int period = config.GetParam("bb_period", Period);
        int trendSma = config.GetParam("bb_trend_sma", TrendSma);
        decimal entryZ = config.GetParam("bb_entry_z", EntryZ);
        if (bars.Count < Math.Max(period, trendSma) + 1) return Hold(config, $"資料不足(需 {Math.Max(period, trendSma) + 1}+ 根)");

        decimal close = bars[^1].Close;
        decimal mean = 0m;
        for (int i = bars.Count - period; i < bars.Count; i++) mean += bars[i].Close;
        mean /= period;
        decimal sq = 0m;
        for (int i = bars.Count - period; i < bars.Count; i++) { var d = bars[i].Close - mean; sq += d * d; }
        decimal std = (decimal)Math.Sqrt((double)(sq / period));
        if (std <= 0m) return Hold(config, "標準差為 0");
        decimal z = (close - mean) / std;

        decimal trend = 0m;
        for (int i = bars.Count - trendSma; i < bars.Count; i++) trend += bars[i].Close;
        trend /= trendSma;
        bool up = close > trend;

        string action; decimal confidence; string reason;
        if (z <= -entryZ && up)
        {
            action = "buy";
            confidence = Math.Clamp(0.6m + (-z - entryZ) * 0.15m, 0.6m, 0.95m);
            reason = $"升勢(close>SMA{trendSma} {trend:F2})回檔 z={z:F2} — 做多";
        }
        else if (z >= entryZ && !up)
        {
            action = "sell";
            confidence = Math.Clamp(0.6m + (z - entryZ) * 0.15m, 0.6m, 0.95m);
            reason = $"跌勢(close<SMA{trendSma} {trend:F2})反彈 z={z:F2} — 做空";
        }
        else
        {
            action = "hold";
            confidence = 0.5m;
            reason = $"z={z:F2}、close-SMA{trendSma} {close - trend:F2} — 不逆大勢、維持";
        }

        return new Signal
        {
            SignalId = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
            Action = action, Confidence = Math.Round(confidence, 2), Reason = reason,
            Interval = config.Interval,
            Indicators = new() { ["z"] = Math.Round(z, 4), ["mean"] = Math.Round(mean, 4), ["trend"] = Math.Round(trend, 4), ["price"] = Math.Round(close, 4) },
        };
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16], Strategy = "bb_revert_ls",
        Symbol = c.Symbol, Exchange = c.Exchange, Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
