using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 通道邊緣回歸(Donchian Fade,多空)—— 突破策略的「反向版」:只在「震盪市(ADX 低)」
/// 才在通道上緣做空、下緣做多,賭區間內假突破會被打回。用 ADX 過濾避免在趨勢市逆勢接刀。
///   ADX < adxMax 且 close ≥ 前 N 根高 → sell(做空、fade 假上破)
///   ADX < adxMax 且 close ≤ 前 N 根低 → buy(做多、fade 假下破)
///   其餘(含趨勢市)→ hold
/// 與 dual_thrust/chandelier(順勢突破)完全相反 → 結構去相關。多空原生。無 lookahead。
/// </summary>
public class DonchianFadeLsStrategy : IStrategy
{
    public string Name => "donchian_fade_ls";
    public string Description => "Donchian Fade(多空)— 震盪市才 fade 通道上緣做空/下緣做多";
    public StrategyCategory Category => StrategyCategory.MeanReversion;
    public int MinBars => 60;
    public decimal MinCapitalUsdt => 120m;

    private const int Lookback = 20;
    private const int AdxPeriod = 14;
    private const decimal AdxMax = 22m;   // 只在 ADX < 此值(震盪市)才 fade

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["df_lookback"]   = new() { Type = "int",     Default = Lookback,  Min = 10, Max = 40, Step = 5, Description = "通道回看根數" },
        ["df_adx_period"] = new() { Type = "int",     Default = AdxPeriod, Min = 7,  Max = 21, Step = 7, Description = "ADX 週期" },
        ["df_adx_max"]    = new() { Type = "decimal", Default = AdxMax,    Min = 15, Max = 30, Step = 2, Description = "震盪市上限(低於才 fade)" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        int lb = config.GetParam("df_lookback", Lookback);
        int adxPeriod = config.GetParam("df_adx_period", AdxPeriod);
        decimal adxMax = config.GetParam("df_adx_max", AdxMax);

        var don = Donchian.Compute(bars, lb);
        var adx = AdxDi.Compute(bars, adxPeriod);
        if (don == null || adx == null) return Hold(config, "指標無法計算(資料不足)");

        decimal close = bars[^1].Close;
        string action = "hold"; decimal confidence = 0.5m; string reason;

        if (adx.Adx >= adxMax)
        {
            reason = $"趨勢市(ADX {adx.Adx:F0}≥{adxMax})— 不逆勢 fade、維持";
        }
        else if (close >= don.PrevUpper)
        {
            action = "sell";
            confidence = 0.7m;
            reason = $"震盪市(ADX {adx.Adx:F0})、close {close:F2} 觸前 {lb} 根高 {don.PrevUpper:F2} — fade 做空";
        }
        else if (close <= don.PrevLower)
        {
            action = "buy";
            confidence = 0.7m;
            reason = $"震盪市(ADX {adx.Adx:F0})、close {close:F2} 觸前 {lb} 根低 {don.PrevLower:F2} — fade 做多";
        }
        else
        {
            reason = $"震盪市但 close {close:F2} 在通道 [{don.PrevLower:F2}, {don.PrevUpper:F2}] 內 — 維持";
        }

        return new Signal
        {
            SignalId = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
            Action = action, Confidence = Math.Round(confidence, 2), Reason = reason,
            Interval = config.Interval,
            Indicators = new() { ["adx"] = adx.Adx, ["prev_upper"] = don.PrevUpper, ["prev_lower"] = don.PrevLower, ["price"] = Math.Round(close, 4) },
        };
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16], Strategy = "donchian_fade_ls",
        Symbol = c.Symbol, Exchange = c.Exchange, Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
