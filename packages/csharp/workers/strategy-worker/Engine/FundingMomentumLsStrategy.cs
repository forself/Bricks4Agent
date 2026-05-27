using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// Funding Momentum LS(2026-05-27 結構性 alpha)— funding_extreme 對照組。
///
/// 發現:funding_extreme(contrarian: funding 低 buy)pool t-stat = -3.76(顯著為負!)。
/// 意義:funding 極端時、市場是「羊群延續」而非「mean revert」。極低 funding 代表空頭強勢、繼續跌;
/// 極高 funding 代表多頭強勢、繼續漲。contrarian 就是 catch falling knife。
///
/// 邏輯(反轉 funding_extreme):
///   funding 在近期分布「極高端」(多頭擁擠付錢)= 多頭趨勢強 → BUY(跟 trend)
///   funding 在「極低端」(空頭擁擠付錢)= 空頭趨勢強 → SELL/SHORT(跟 trend)
///   中性區 = hold
///
/// 跟 funding_extreme 共用 FundingBias.Compute 指標、只是 buy/sell 方向翻轉。
/// LS 版:跟核心 tsmom 同 LS 引擎、可開空、不只 buy/exit。
/// </summary>
public class FundingMomentumLsStrategy : IStrategy
{
    public string Name => "funding_momentum_ls";
    public string Description => "Funding Momentum LS — funding 極高 = 多頭趨勢強進多、極低 = 空頭趨勢強進空(跟趨勢、不是 contrarian)";
    public StrategyCategory Category => StrategyCategory.Trend;
    public int MinBars => 40;
    public decimal MinCapitalUsdt => 100m;

    private const decimal HotPct  = 0.85m;     // ≥ 此 = 多頭擁擠 → BUY 跟趨勢
    private const decimal ColdPct = 0.15m;     // ≤ 此 = 空頭擁擠 → SELL 跟趨勢
    private const int Lookback = 100;

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["funding_hot_pct"]  = new() { Type = "decimal", Default = 0.85m, Choices = new object[] { 0.75m, 0.8m, 0.85m, 0.9m }, Description = "BUY 門檻(funding 百分位上界)" },
        ["funding_cold_pct"] = new() { Type = "decimal", Default = 0.15m, Choices = new object[] { 0.1m, 0.15m, 0.2m, 0.25m }, Description = "SELL 門檻(funding 百分位下界)" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        if (bars.Count < MinBars) return Hold(config, $"Not enough data (need {MinBars}+ bars)");

        var hotPct  = config.GetParam("funding_hot_pct", HotPct);
        var coldPct = config.GetParam("funding_cold_pct", ColdPct);

        var fb = FundingBias.Compute(bars, Lookback);
        if (fb == null) return Hold(config, "無 funding 資料(非 perp 或未接資金費率)— 自動降級");
        var (rate, pct, oiChange) = fb.Value;

        string action = "hold";
        decimal confidence = 0.5m;
        string reason;

        if (pct >= hotPct)
        {
            action = "buy";
            confidence = Math.Clamp(0.6m + (pct - hotPct), 0.5m, 0.9m);
            reason = $"funding {rate:F6} 在近期極高端(百分位 {pct:P0} ≥ {hotPct:P0})= 多頭趨勢強 → 跟單做多";
        }
        else if (pct <= coldPct)
        {
            action = "sell";
            confidence = Math.Clamp(0.6m + (coldPct - pct), 0.5m, 0.9m);
            reason = $"funding {rate:F6} 在近期極低端(百分位 {pct:P0} ≤ {coldPct:P0})= 空頭趨勢強 → 跟單做空";
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
        Strategy = "funding_momentum_ls",
        Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
