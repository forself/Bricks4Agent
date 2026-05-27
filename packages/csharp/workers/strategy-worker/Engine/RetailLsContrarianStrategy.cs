using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// Retail Long/Short Ratio Contrarian(2026-05-28 Q2 結構性 alpha)。
///
/// 發現(oi-validate 跨 8 幣 × 1y IS + OOS 雙確認):
///   - IS pool linear t = -2.24、quantile t = -2.18(2025-05-27 → 2026-05-27)
///   - OOS pool linear t = -2.89、quantile t = -2.25(2024-05-28 → 2025-05-27、跨年更強)
///   - 跟 funding corr 0.04(獨立 alpha 源、不重複 [[funding_momentum_ls]])
///   - 8 幣 linear t-stat 方向全為負(BNB -2.11, XRP -1.80, DOGE -1.45, ETH -0.92, ADA -0.76, BTC -0.65, AVAX -0.24, SOL -0.23)
///
/// 經濟意義:散戶位置擁擠 → 反向走(經典 contrarian)。
///   retail_ls_ratio > 1 = 散戶看多比看空多
///   retail_ls_ratio 在近期分布「極高端」(散戶極度看多)→ SHORT(反指)
///   retail_ls_ratio 在「極低端」(散戶極度看空)→ LONG(反指)
///
/// 資料來源:Binance futures /data/futures/um/daily/metrics/{symbol}/*.zip 的 count_long_short_ratio。
/// 由 caller 把 retail L/S 填進 BarData.RetailLongShortRatio(無資料整條回 hold、自動降級)。
///
/// **重要 entry timing**:retail_ls 跟當天 return corr -0.18,雖不算 lookahead 但散戶比例會被當天價格反向影響
///   (跌時散戶追多)。Evaluate 看 bars[^1] 是「已收盤的最新 bar」、開倉在下一根 bar open,正確避 lookahead。
/// </summary>
public class RetailLsContrarianStrategy : IStrategy
{
    private readonly string _name;
    private readonly decimal _hotPct;
    private readonly decimal _coldPct;
    private readonly bool _dailyRebalance;
    private readonly bool _invertSignal;   // true = 跟單散戶(momentum)、false = 反向(contrarian、預設)

    public RetailLsContrarianStrategy(string name = "retail_ls_contrarian", decimal hotPct = 0.80m, decimal coldPct = 0.20m, bool dailyRebalance = false, bool invertSignal = false)
    {
        _name = name;
        _hotPct = hotPct;
        _coldPct = coldPct;
        _dailyRebalance = dailyRebalance;
        _invertSignal = invertSignal;
    }

    public string Name => _name;
    public string Description => $"Retail L/S Contrarian — 散戶極度看多 → 反向 SHORT、極度看空 → 反向 LONG(threshold {_coldPct:P0}/{_hotPct:P0})";
    public StrategyCategory Category => StrategyCategory.MeanReversion;
    public int MinBars => 40;
    public decimal MinCapitalUsdt => 100m;

    private const int Lookback = 100;

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["ls_hot_pct"]  = new() { Type = "decimal", Default = 0.80m, Choices = new object[] { 0.75m, 0.80m, 0.85m, 0.90m }, Description = "SHORT 門檻(散戶 L/S 百分位上界)" },
        ["ls_cold_pct"] = new() { Type = "decimal", Default = 0.20m, Choices = new object[] { 0.10m, 0.15m, 0.20m, 0.25m }, Description = "LONG 門檻(散戶 L/S 百分位下界)" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        if (bars.Count < MinBars) return Hold(config, $"Not enough data (need {MinBars}+ bars)");

        var hotPct = config.GetParam("ls_hot_pct", _hotPct);
        var coldPct = config.GetParam("ls_cold_pct", _coldPct);

        int n = Math.Min(Lookback, bars.Count);
        var window = bars.GetRange(bars.Count - n, n);
        var ratios = new List<decimal>();
        foreach (var b in window) if (b.RetailLongShortRatio.HasValue) ratios.Add(b.RetailLongShortRatio.Value);
        if (ratios.Count < 20) return Hold(config, "無 retail_ls 資料(非 perp 或未接 data.binance.vision metrics)— 自動降級");

        decimal current = ratios[^1];
        int leq = 0;
        foreach (var r in ratios) if (r <= current) leq++;
        decimal pct = (decimal)leq / ratios.Count;

        string action = "hold";
        decimal confidence = 0.5m;
        string reason;

        // 對立假設備援:invertSignal=true → 跟單散戶(momentum),測「也許散戶在極端時是對的」
        // contrarian 是預設、Q2 oi-validate t=-2.89/-2.25 雙確認的方向
        string highAction = _invertSignal ? "buy" : "sell";    // 散戶極多 → 跟單 buy / 反向 sell
        string lowAction  = _invertSignal ? "sell" : "buy";   // 散戶極空 → 跟單 sell / 反向 buy
        string highReasonTag = _invertSignal ? "跟單 LONG" : "反向 SHORT";
        string lowReasonTag  = _invertSignal ? "跟單 SHORT" : "反向 LONG";

        if (_dailyRebalance)
        {
            // Daily rebalance 模式:每根都 emit buy/sell、position 每天 flip 按 pctile
            if (pct > 0.5m)
            {
                action = highAction;
                confidence = Math.Clamp(0.6m + (pct - 0.5m), 0.6m, 0.9m);
                reason = $"[daily-rebal] retail L/S 百分位 {pct:P0} > 50% → {highReasonTag}(下一根結算)";
            }
            else
            {
                action = lowAction;
                confidence = Math.Clamp(0.6m + (0.5m - pct), 0.6m, 0.9m);
                reason = $"[daily-rebal] retail L/S 百分位 {pct:P0} ≤ 50% → {lowReasonTag}(下一根結算)";
            }
        }
        else if (pct >= hotPct)
        {
            action = highAction;
            confidence = Math.Clamp(0.6m + (pct - hotPct), 0.5m, 0.9m);
            reason = $"retail L/S {current:F3} 在近期極高端(百分位 {pct:P0} ≥ {hotPct:P0})= 散戶極度擁擠看多 → {highReasonTag}";
        }
        else if (pct <= coldPct)
        {
            action = lowAction;
            confidence = Math.Clamp(0.6m + (coldPct - pct), 0.5m, 0.9m);
            reason = $"retail L/S {current:F3} 在近期極低端(百分位 {pct:P0} ≤ {coldPct:P0})= 散戶極度擁擠看空 → {lowReasonTag}";
        }
        else
        {
            reason = $"retail L/S 百分位 {pct:P0} 在中性區 [{coldPct:P0}, {hotPct:P0}] — 觀望";
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
                ["retail_ls_ratio"] = Math.Round(current, 4),
                ["retail_ls_pctile"] = pct,
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
