using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// OI Momentum Quantile LS(2026-05-28 Q2 結構性 alpha 第二個)。
///
/// 發現(oi-validate 跨 8 幣 × 1y IS + OOS 雙確認):
///   - IS pool quantile t = +2.01(2025-05-27 → 2026-05-27)
///   - OOS pool quantile t = +2.39(2024-05-28 → 2025-05-27、跨年更強)
///   - Linear Pearson IS+1.25/OOS+0.44 不顯著、edge 在「OI 暴衝」極端值區段(非線性)
///   - 跟 funding corr 中(0.1)、跟 retail_ls 獨立
///
/// 經濟意義:OI 大幅增加 = 新資金大量進場、趨勢有續航 → 跟單動量。
///   注意:OI 跟當天 return corr ~0.7(price 同向),所以 OI %change 本質帶 price momentum 成分。
///   raw linear edge 不顯著(被 price 代理沖淡)、但 extreme OI 衝高事件本身有獨立預測力。
///
/// 跟 retail_ls_contrarian 的差異:
///   - retail_ls 是 contrarian(散戶看多 → 跌)
///   - OI momentum 是 trend-following(OI 衝高 → 漲)
///   - 兩條互補、可組 portfolio sleeve
///
/// 用 BarData.OpenInterest 計算 daily %change、算 pctile 與 lookback 比較。
/// </summary>
public class OiMomentumLsStrategy : IStrategy
{
    private readonly string _name;
    private readonly decimal _hotPct;
    private readonly decimal _coldPct;

    public OiMomentumLsStrategy(string name = "oi_momentum_ls", decimal hotPct = 0.80m, decimal coldPct = 0.20m)
    {
        _name = name;
        _hotPct = hotPct;
        _coldPct = coldPct;
    }

    public string Name => _name;
    public string Description => $"OI Momentum LS — OI %change 在極高端 → LONG(動量延續)、極低端 → SHORT(threshold {_coldPct:P0}/{_hotPct:P0})";
    public StrategyCategory Category => StrategyCategory.Trend;
    public int MinBars => 40;
    public decimal MinCapitalUsdt => 100m;

    private const int Lookback = 100;

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["oi_hot_pct"]  = new() { Type = "decimal", Default = 0.80m, Choices = new object[] { 0.75m, 0.80m, 0.85m, 0.90m }, Description = "LONG 門檻(OI %change 百分位上界)" },
        ["oi_cold_pct"] = new() { Type = "decimal", Default = 0.20m, Choices = new object[] { 0.10m, 0.15m, 0.20m, 0.25m }, Description = "SHORT 門檻(OI %change 百分位下界)" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        if (bars.Count < MinBars) return Hold(config, $"Not enough data (need {MinBars}+ bars)");

        var hotPct = config.GetParam("oi_hot_pct", _hotPct);
        var coldPct = config.GetParam("oi_cold_pct", _coldPct);

        int n = Math.Min(Lookback, bars.Count);
        var window = bars.GetRange(bars.Count - n, n);

        // 算 OI %change 序列(需相鄰兩根都有 OI)
        var changes = new List<decimal>();
        for (int i = 1; i < window.Count; i++)
        {
            if (window[i].OpenInterest is decimal cur && cur > 0m &&
                window[i - 1].OpenInterest is decimal prev && prev > 0m)
            {
                changes.Add((cur - prev) / prev);
            }
        }
        if (changes.Count < 20) return Hold(config, "無 OI 資料(非 perp 或未接 data.binance.vision metrics)— 自動降級");

        decimal current = changes[^1];
        int leq = 0;
        foreach (var c in changes) if (c <= current) leq++;
        decimal pct = (decimal)leq / changes.Count;

        string action = "hold";
        decimal confidence = 0.5m;
        string reason;

        if (pct >= hotPct)
        {
            action = "buy";
            confidence = Math.Clamp(0.6m + (pct - hotPct), 0.5m, 0.9m);
            reason = $"OI %change {current:P2} 在近期極高端(百分位 {pct:P0} ≥ {hotPct:P0})= 大量新資金進場 → 跟趨勢 LONG";
        }
        else if (pct <= coldPct)
        {
            action = "sell";
            confidence = Math.Clamp(0.6m + (coldPct - pct), 0.5m, 0.9m);
            reason = $"OI %change {current:P2} 在近期極低端(百分位 {pct:P0} ≤ {coldPct:P0})= 大量平倉/空頭增 → SHORT";
        }
        else
        {
            reason = $"OI %change 百分位 {pct:P0} 在中性區 [{coldPct:P0}, {hotPct:P0}] — 觀望";
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
                ["oi_change_pct"] = Math.Round(current, 6),
                ["oi_pctile"] = pct,
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
