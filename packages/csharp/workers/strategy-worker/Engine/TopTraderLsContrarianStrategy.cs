using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// Top Trader Long/Short Ratio Contrarian / Momentum(2026-05-28 Q2 翻案測試)。
///
/// oi-validate 顯示 Top L/S 跟 funding corr +0.61(高、共同源),曾被 dismiss 為「不算新 alpha」。
/// 翻案理由:即使 corr 高,在極端區 contrarian 可能仍有 residual edge,strat-validate 才能驗。
///
/// 資料:Binance metrics CSV 的 sum_toptrader_long_short_ratio(大戶持倉多空比)。
/// 重用 BarData.RetailLongShortRatio 欄位 — 由 caller(strat-validate)注入時用 top L/S 而非 retail L/S。
/// (這代表執行時、retail_ls vs top_ls 不能同框混用、要分開測。Production 要再加 BarData.TopLsRatio 欄。)
///
/// 預設 contrarian(跟 funding_extreme 反向、應該 work):
///   Top L/S 極高(大戶超看多)→ SHORT(因為大戶 funding 已付高了、過熱)
///   Top L/S 極低 → LONG
/// </summary>
public class TopTraderLsContrarianStrategy : IStrategy
{
    private readonly string _name;
    private readonly decimal _hotPct;
    private readonly decimal _coldPct;
    private readonly bool _invertSignal;

    public TopTraderLsContrarianStrategy(string name = "top_ls_contrarian", decimal hotPct = 0.80m, decimal coldPct = 0.20m, bool invertSignal = false)
    {
        _name = name;
        _hotPct = hotPct;
        _coldPct = coldPct;
        _invertSignal = invertSignal;
    }

    public string Name => _name;
    public string Description => $"Top Trader L/S Contrarian/Momentum — 大戶 L/S 極端 → 反向(預設)或跟單(threshold {_coldPct:P0}/{_hotPct:P0})";
    public StrategyCategory Category => _invertSignal ? StrategyCategory.Trend : StrategyCategory.MeanReversion;
    public int MinBars => 40;
    public decimal MinCapitalUsdt => 100m;

    private const int Lookback = 100;

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["ls_hot_pct"]  = new() { Type = "decimal", Default = 0.80m, Choices = new object[] { 0.75m, 0.80m, 0.85m, 0.90m }, Description = "高極端門檻" },
        ["ls_cold_pct"] = new() { Type = "decimal", Default = 0.20m, Choices = new object[] { 0.10m, 0.15m, 0.20m, 0.25m }, Description = "低極端門檻" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        if (bars.Count < MinBars) return Hold(config, $"Not enough data (need {MinBars}+ bars)");

        var hotPct = config.GetParam("ls_hot_pct", _hotPct);
        var coldPct = config.GetParam("ls_cold_pct", _coldPct);

        int n = Math.Min(Lookback, bars.Count);
        var window = bars.GetRange(bars.Count - n, n);

        // 注意:此處 reuse RetailLongShortRatio 欄位(strat-validate 注入時可指定哪個 metric)。
        // Production 部署若要跟 retail_ls 同 broker 並行,必須加獨立 BarData.TopLsRatio 欄位。
        var ratios = new List<decimal>();
        foreach (var b in window) if (b.RetailLongShortRatio.HasValue) ratios.Add(b.RetailLongShortRatio.Value);
        if (ratios.Count < 20) return Hold(config, "無 L/S 資料、自動降級");

        decimal current = ratios[^1];
        int leq = 0;
        foreach (var r in ratios) if (r <= current) leq++;
        decimal pct = (decimal)leq / ratios.Count;

        string action = "hold";
        decimal confidence = 0.5m;
        string reason;

        string highAction = _invertSignal ? "buy" : "sell";
        string lowAction = _invertSignal ? "sell" : "buy";

        if (pct >= hotPct)
        {
            action = highAction;
            confidence = Math.Clamp(0.6m + (pct - hotPct), 0.5m, 0.9m);
            reason = $"top L/S {current:F3} 極高(百分位 {pct:P0})→ {(_invertSignal ? "跟單 LONG" : "反向 SHORT")}";
        }
        else if (pct <= coldPct)
        {
            action = lowAction;
            confidence = Math.Clamp(0.6m + (coldPct - pct), 0.5m, 0.9m);
            reason = $"top L/S {current:F3} 極低(百分位 {pct:P0})→ {(_invertSignal ? "跟單 SHORT" : "反向 LONG")}";
        }
        else
        {
            reason = $"top L/S 百分位 {pct:P0} 中性區 — 觀望";
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
            Indicators = new() { ["top_ls_ratio"] = Math.Round(current, 4), ["top_ls_pctile"] = pct },
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
