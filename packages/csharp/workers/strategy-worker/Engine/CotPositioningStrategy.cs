using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// COT 持倉反向(2026-06-11 從零開發、結構性部位 alpha — 商品/FX/股指期)。
///
/// 機制:CFTC COT 投機者(non-commercial)淨持倉 % OI。
///   - 投機者極端淨多(高百分位)= 擁擠多單 → 反向 SHORT(跟商業避險者/聰明錢站對面)
///   - 投機者極端淨空(低百分位)= 擁擠空單 → 反向 LONG
///   - invert=true 改測「跟投機者」(動量假設)
///
/// 學術:Bessembinder & Chan 1992 — hedger 預測力 > 投機者。跟價格 TA 天生去相關 = 結構性 alpha。
/// 用 BarData.CotSpecNet(strat-validate 從 CFTC Socrata 注入、無 lookahead)。
/// </summary>
public class CotPositioningStrategy : IStrategy
{
    private readonly string _name;
    private readonly decimal _hotPct;
    private readonly decimal _coldPct;
    private readonly bool _invert;

    public CotPositioningStrategy(string name = "cot_positioning", decimal hotPct = 0.85m, decimal coldPct = 0.15m, bool invert = false)
    {
        _name = name;
        _hotPct = hotPct;
        _coldPct = coldPct;
        _invert = invert;
    }

    public string Name => _name;
    public string Description => $"COT 持倉反向 — 投機者淨持倉百分位 ≥{_hotPct:P0} → {(_invert ? "LONG(跟投機)" : "SHORT(反向擁擠)")};≤{_coldPct:P0} → 反向";
    public StrategyCategory Category => StrategyCategory.MeanReversion;
    public int MinBars => 120;
    public decimal MinCapitalUsdt => 100m;

    private const int Lookback = 252;

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["cot_hot_pct"]  = new() { Type = "decimal", Default = 0.85m, Choices = new object[] { 0.80m, 0.85m, 0.90m, 0.95m }, Description = "極端淨多門檻(百分位上界)" },
        ["cot_cold_pct"] = new() { Type = "decimal", Default = 0.15m, Choices = new object[] { 0.05m, 0.10m, 0.15m, 0.20m }, Description = "極端淨空門檻(百分位下界)" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        if (bars.Count < MinBars) return Hold(config, $"Not enough data (need {MinBars}+ bars)");
        var hotPct = config.GetParam("cot_hot_pct", _hotPct);
        var coldPct = config.GetParam("cot_cold_pct", _coldPct);

        int n = Math.Min(Lookback, bars.Count);
        var window = bars.GetRange(bars.Count - n, n);
        var vals = new List<decimal>();
        foreach (var b in window) if (b.CotSpecNet is decimal c) vals.Add(c);
        if (vals.Count < 60) return Hold(config, "無 COT 資料(非期貨或未注入)— 自動降級");

        decimal cur = vals[^1];
        int leq = 0;
        foreach (var v in vals) if (v <= cur) leq++;
        decimal pct = (decimal)leq / vals.Count;

        string hiAction = _invert ? "buy" : "sell";   // 擁擠多 → 預設反向 SHORT
        string loAction = _invert ? "sell" : "buy";

        string action = "hold";
        decimal confidence = 0.5m;
        string reason;

        if (pct >= hotPct)
        {
            action = hiAction;
            confidence = Math.Clamp(0.6m + (pct - hotPct), 0.5m, 0.9m);
            reason = $"投機者淨持倉 {cur:P1} 在極高端(百分位 {pct:P0} ≥ {hotPct:P0})= 擁擠多 → {(_invert ? "跟投機 LONG" : "反向 SHORT")}";
        }
        else if (pct <= coldPct)
        {
            action = loAction;
            confidence = Math.Clamp(0.6m + (coldPct - pct), 0.5m, 0.9m);
            reason = $"投機者淨持倉 {cur:P1} 在極低端(百分位 {pct:P0} ≤ {coldPct:P0})= 擁擠空 → {(_invert ? "跟投機 SHORT" : "反向 LONG")}";
        }
        else
        {
            reason = $"投機者淨持倉百分位 {pct:P0} 中性 [{coldPct:P0}, {hotPct:P0}] — 觀望";
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
                ["cot_spec_net"] = Math.Round(cur, 4),
                ["cot_pctile"] = pct,
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
