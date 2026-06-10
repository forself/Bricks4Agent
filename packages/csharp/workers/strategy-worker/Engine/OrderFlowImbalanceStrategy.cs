using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// Order-flow 失衡(2026-06-10 從零開發、crypto 盤中微結構)。
///
/// 機制:taker 主動買/賣量比(sum_taker_long_short_vol_ratio)= 攻擊性訂單流。
///   - 主動買壓在高百分位(taker 大買)→ 短期續航 → LONG(知情/動量訂單流)
///   - 主動賣壓在低百分位(taker 大賣)→ SHORT
///   - invert=true 則做反轉假設(極端攻擊買=散戶 FOMO 反轉)
///
/// 學術上 order-flow imbalance(OFI)預測短期報酬是真效應 → 盤中(1h)才有意義(日線抹平)。
/// 用 BarData.TakerLsRatio(strat-validate 從 data.binance.vision 5min metrics 注入、聚合到 bar tf)。
/// </summary>
public class OrderFlowImbalanceStrategy : IStrategy
{
    private readonly string _name;
    private readonly decimal _hotPct;
    private readonly decimal _coldPct;
    private readonly bool _invert;

    public OrderFlowImbalanceStrategy(string name = "order_flow", decimal hotPct = 0.80m, decimal coldPct = 0.20m, bool invert = false)
    {
        _name = name;
        _hotPct = hotPct;
        _coldPct = coldPct;
        _invert = invert;
    }

    public string Name => _name;
    public string Description => $"Order-flow 失衡 — taker 買/賣量比百分位 ≥{_hotPct:P0} → {(_invert ? "SHORT(反轉)" : "LONG(續航)")};≤{_coldPct:P0} → 反向";
    public StrategyCategory Category => StrategyCategory.Trend;
    public int MinBars => 40;
    public decimal MinCapitalUsdt => 100m;

    private const int Lookback = 100;

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["of_hot_pct"]  = new() { Type = "decimal", Default = 0.80m, Choices = new object[] { 0.75m, 0.80m, 0.85m, 0.90m }, Description = "LONG 門檻(taker 買壓百分位上界)" },
        ["of_cold_pct"] = new() { Type = "decimal", Default = 0.20m, Choices = new object[] { 0.10m, 0.15m, 0.20m, 0.25m }, Description = "SHORT 門檻(taker 賣壓百分位下界)" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        if (bars.Count < MinBars) return Hold(config, $"Not enough data (need {MinBars}+ bars)");
        var hotPct = config.GetParam("of_hot_pct", _hotPct);
        var coldPct = config.GetParam("of_cold_pct", _coldPct);

        int n = Math.Min(Lookback, bars.Count);
        var window = bars.GetRange(bars.Count - n, n);
        var vals = new List<decimal>();
        foreach (var b in window) if (b.TakerLsRatio is decimal t && t > 0m) vals.Add(t);
        if (vals.Count < 20) return Hold(config, "無 taker order-flow 資料(非 perp 或未接 metrics)— 自動降級");

        decimal cur = vals[^1];
        int leq = 0;
        foreach (var v in vals) if (v <= cur) leq++;
        decimal pct = (decimal)leq / vals.Count;

        string hiAction = _invert ? "sell" : "buy";
        string loAction = _invert ? "buy" : "sell";

        string action = "hold";
        decimal confidence = 0.5m;
        string reason;

        if (pct >= hotPct)
        {
            action = hiAction;
            confidence = Math.Clamp(0.6m + (pct - hotPct), 0.5m, 0.9m);
            reason = $"taker 買壓百分位 {pct:P0} ≥ {hotPct:P0}(主動買盤強)→ {(_invert ? "反轉 SHORT" : "續航 LONG")}";
        }
        else if (pct <= coldPct)
        {
            action = loAction;
            confidence = Math.Clamp(0.6m + (coldPct - pct), 0.5m, 0.9m);
            reason = $"taker 賣壓百分位 {pct:P0} ≤ {coldPct:P0}(主動賣盤強)→ {(_invert ? "反轉 LONG" : "續航 SHORT")}";
        }
        else
        {
            reason = $"taker 買賣百分位 {pct:P0} 中性 [{coldPct:P0}, {hotPct:P0}] — 觀望";
        }

        return new Signal
        {
            SignalId = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy = Name,
            Symbol = config.Symbol,
            Exchange = config.Exchange,
            Action = action,
            Confidence = Math.Round(confidence, 2),
            Reason = reason,
            Interval = config.Interval,
            Indicators = new()
            {
                ["taker_ratio"] = Math.Round(cur, 4),
                ["taker_pctile"] = pct,
            },
        };
    }

    private Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = _name,
        Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
