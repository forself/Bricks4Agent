using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 資金費率極端反轉(Funding Extreme)—— 第一個「方向性」非價格因子策略,用來回答
/// 「funding 到底有沒有 OOS edge」這個更根本的問題(之前 funding 只當風控閘門、沒單獨驗證過)。
///
/// 邏輯(永續經典 contrarian、均值回歸味):
///   funding 在近期分布的「極低端」= 空頭過度擁擠(空頭付錢給多頭)→ 軋空風險 → 偏多 → buy。
///   funding 在「極高端」= 多頭過度擁擠(多頭付錢)→ 過熱 → 出場(回測引擎多單 only,sell=平倉)。
///
/// 跟價格技術指標正交:它看的是「資金站哪邊、多擠」,不是價格型態。
/// 資料來源 = BarData.FundingRate(批次回測走 get_bars_funding as-of join 餵進來;無 perp 資料則 hold)。
/// </summary>
public class FundingExtremeStrategy : IStrategy
{
    public string Name => "funding_extreme";
    public string Description => "Funding Extreme — 資金費率極低(空頭擁擠)contrarian 進多、極高(多頭擁擠)出場";
    public StrategyCategory Category => StrategyCategory.MeanReversion;
    public int MinBars => 40;                  // FundingBias 要 ≥20 bar 且 ≥10 筆 funding
    public decimal MinCapitalUsdt => 100m;

    private const decimal ColdPct = 0.15m;     // funding 百分位 ≤ 此 = 空頭擁擠 → 進多
    private const decimal HotPct  = 0.85m;     // ≥ 此 = 多頭擁擠 → 出場
    private const int Lookback = 100;

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["funding_cold_pct"] = new() { Type = "decimal", Default = 0.15m, Choices = new object[] { 0.1m, 0.15m, 0.2m, 0.25m }, Description = "進多門檻(funding 百分位下界、空頭擁擠)" },
        ["funding_hot_pct"]  = new() { Type = "decimal", Default = 0.85m, Choices = new object[] { 0.75m, 0.8m, 0.85m, 0.9m }, Description = "出場門檻(funding 百分位上界、多頭擁擠)" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        if (bars.Count < MinBars) return Hold(config, $"Not enough data (need {MinBars}+ bars)");

        var coldPct = config.GetParam("funding_cold_pct", ColdPct);
        var hotPct  = config.GetParam("funding_hot_pct", HotPct);

        var fb = FundingBias.Compute(bars, Lookback);
        if (fb == null) return Hold(config, "無 funding 資料(非 perp 或未接資金費率)— 自動降級");
        var (rate, pct, oiChange) = fb.Value;

        string action = "hold";
        decimal confidence = 0.5m;
        string reason;

        if (pct <= coldPct)
        {
            action = "buy";
            // 越極端(百分位越低)信心越高;最低 0.6 才會觸發進場
            confidence = Math.Clamp(0.6m + (coldPct - pct), 0.5m, 0.9m);
            reason = $"funding {rate:F6} 在近期極低端(百分位 {pct:P0} ≤ {coldPct:P0})= 空頭擁擠 → contrarian 進多";
        }
        else if (pct >= hotPct)
        {
            action = "sell";
            confidence = Math.Clamp(0.6m + (pct - hotPct), 0.5m, 0.9m);
            reason = $"funding {rate:F6} 在近期極高端(百分位 {pct:P0} ≥ {hotPct:P0})= 多頭擁擠 → 出場";
        }
        else
        {
            reason = $"funding 百分位 {pct:P0} 在中性區 [{coldPct:P0}, {hotPct:P0}] — 觀望";
        }

        return new Signal
        {
            SignalId   = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy   = Name,
            Symbol     = config.Symbol,
            Exchange   = config.Exchange,
            Action     = action,
            Confidence = Math.Round(confidence, 2),
            Reason     = reason,
            Interval   = config.Interval,
            Indicators = new()
            {
                ["funding_rate"]   = Math.Round(rate, 6),
                ["funding_pctile"] = pct,
                ["oi_change_pct"]  = oiChange,
            },
        };
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = "funding_extreme",
        Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
