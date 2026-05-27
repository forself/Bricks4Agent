using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// Retail L/S Delta(變化率)Contrarian — Q2 第二代結構性 alpha(2026-05-28 翻案發現)。
///
/// 發現(oi-validate 跨 8 幣 IS+OOS 雙確認、比 raw 還更強):
///   - IS  pool linear t = **-3.51** ✅、quantile t = **-3.18** ✅
///   - OOS pool linear t = **-3.65** ✅、quantile t = **-2.91** ✅
///   - 比 retail_ls raw 的 IS/OOS -2.24/-2.89/-2.18/-2.25 全面更顯著
///
/// 經濟意義:**散戶意見變化方向比絕對位置更能預測 next return**。
///   - retail_ls_ratio Δ > 0(散戶剛從不看多 → 變看多)= 動量耗盡頂部 → SHORT
///   - retail_ls_ratio Δ < 0(散戶剛從看多 → 變不看多)= 動量耗盡底部 → LONG
///   - level 是 lag、delta 是 acceleration、acceleration 含更多前瞻資訊
///
/// 跟 raw retail_ls_contrarian 雖然同源、但測量角度不同:
///   - raw 看「現在有多擠」
///   - delta 看「擠的速度」(剛開始擁擠 vs 已經擁擠很久)
///   - 兩者可組 portfolio sleeve、互相確認(都極端 → confidence 加倍)
///
/// 用 BarData.RetailLongShortRatio 連兩根 bar 算 delta、用 100 bar lookback 算 pctile。
/// </summary>
public class RetailLsDeltaContrarianStrategy : IStrategy
{
    private readonly string _name;
    private readonly decimal _hotPct;
    private readonly decimal _coldPct;
    private readonly bool _invertSignal;

    public RetailLsDeltaContrarianStrategy(string name = "retail_ls_delta_contrarian", decimal hotPct = 0.80m, decimal coldPct = 0.20m, bool invertSignal = false)
    {
        _name = name;
        _hotPct = hotPct;
        _coldPct = coldPct;
        _invertSignal = invertSignal;
    }

    public string Name => _name;
    public string Description => $"Retail L/S Δ Contrarian — 散戶意見變化方向反指(threshold {_coldPct:P0}/{_hotPct:P0})";
    public StrategyCategory Category => StrategyCategory.MeanReversion;
    public int MinBars => 40;
    public decimal MinCapitalUsdt => 100m;

    private const int Lookback = 100;

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["delta_hot_pct"]  = new() { Type = "decimal", Default = 0.80m, Choices = new object[] { 0.75m, 0.80m, 0.85m, 0.90m }, Description = "SHORT 門檻(Δ 百分位上界)" },
        ["delta_cold_pct"] = new() { Type = "decimal", Default = 0.20m, Choices = new object[] { 0.10m, 0.15m, 0.20m, 0.25m }, Description = "LONG 門檻(Δ 百分位下界)" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        if (bars.Count < MinBars) return Hold(config, $"Not enough data (need {MinBars}+ bars)");

        var hotPct = config.GetParam("delta_hot_pct", _hotPct);
        var coldPct = config.GetParam("delta_cold_pct", _coldPct);

        int n = Math.Min(Lookback, bars.Count);
        var window = bars.GetRange(bars.Count - n, n);

        // 算 retail_ls Δ 序列(相鄰兩根都有 ratio 才算)
        var deltas = new List<decimal>();
        for (int i = 1; i < window.Count; i++)
        {
            if (window[i].RetailLongShortRatio is decimal cur &&
                window[i - 1].RetailLongShortRatio is decimal prev)
            {
                deltas.Add(cur - prev);
            }
        }
        if (deltas.Count < 20) return Hold(config, "無 retail_ls 資料(非 perp 或未接 data.binance.vision)— 自動降級");

        decimal current = deltas[^1];
        int leq = 0;
        foreach (var d in deltas) if (d <= current) leq++;
        decimal pct = (decimal)leq / deltas.Count;

        string action = "hold";
        decimal confidence = 0.5m;
        string reason;

        string highAction = _invertSignal ? "buy" : "sell";
        string lowAction = _invertSignal ? "sell" : "buy";

        if (pct >= hotPct)
        {
            action = highAction;
            confidence = Math.Clamp(0.6m + (pct - hotPct), 0.5m, 0.9m);
            reason = $"retail L/S Δ {current:+0.000;-0.000} 在近期極高端(百分位 {pct:P0} ≥ {hotPct:P0})= 散戶剛加入擁擠 → {(_invertSignal ? "跟單" : "反向 SHORT")}";
        }
        else if (pct <= coldPct)
        {
            action = lowAction;
            confidence = Math.Clamp(0.6m + (coldPct - pct), 0.5m, 0.9m);
            reason = $"retail L/S Δ {current:+0.000;-0.000} 在近期極低端(百分位 {pct:P0} ≤ {coldPct:P0})= 散戶剛放棄擁擠 → {(_invertSignal ? "跟單" : "反向 LONG")}";
        }
        else
        {
            reason = $"retail L/S Δ 百分位 {pct:P0} 在中性區 [{coldPct:P0}, {hotPct:P0}] — 觀望";
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
                ["retail_ls_delta"] = Math.Round(current, 6),
                ["retail_ls_delta_pctile"] = pct,
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
