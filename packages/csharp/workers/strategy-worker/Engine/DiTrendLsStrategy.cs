using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// DI 方向趨勢(多空)—— Wilder DMI:趨勢夠強(ADX≥門檻)時順 +DI/−DI 方向站隊,
/// 趨勢太弱(ADX<門檻)就空手。+DI>−DI 做多、−DI>+DI 做空。
/// 用已測過的 AdxDi 指標;只在有趨勢時出手 → 避開盤整鋸齒。多空原生、穩定正期望。無 lookahead。
/// </summary>
public class DiTrendLsStrategy : IStrategy
{
    public string Name => "di_trend_ls";
    public string Description => "DI 方向趨勢(多空)— ADX 夠強時順 DI 方向做多/做空";
    public StrategyCategory Category => StrategyCategory.Trend;
    public int MinBars => 40;
    public decimal MinCapitalUsdt => 100m;

    private const int AdxPeriod = 14;
    private const decimal AdxMin = 20m;

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["di_adx_period"] = new() { Type = "int",     Default = AdxPeriod, Min = 7,  Max = 21, Step = 7, Description = "ADX/DI 週期" },
        ["di_adx_min"]    = new() { Type = "decimal", Default = AdxMin,    Min = 15, Max = 30, Step = 5, Description = "出手所需最低趨勢強度" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        int adxPeriod = config.GetParam("di_adx_period", AdxPeriod);
        decimal adxMin = config.GetParam("di_adx_min", AdxMin);

        var adx = AdxDi.Compute(bars, adxPeriod);
        if (adx == null) return Hold(config, "ADX 無法計算(資料不足)");

        string action; decimal confidence; string reason;
        if (adx.Adx < adxMin)
        {
            action = "hold";
            confidence = 0.5m;
            reason = $"ADX {adx.Adx:F0} < {adxMin} 趨勢太弱 — 空手";
        }
        else if (adx.PlusDi > adx.MinusDi)
        {
            action = "buy";
            confidence = Math.Clamp(0.6m + (adx.PlusDi - adx.MinusDi) / 100m, 0.6m, 0.95m);
            reason = $"ADX {adx.Adx:F0}≥{adxMin}、+DI {adx.PlusDi:F0}>−DI {adx.MinusDi:F0} — 順勢做多";
        }
        else
        {
            action = "sell";
            confidence = Math.Clamp(0.6m + (adx.MinusDi - adx.PlusDi) / 100m, 0.6m, 0.95m);
            reason = $"ADX {adx.Adx:F0}≥{adxMin}、−DI {adx.MinusDi:F0}>+DI {adx.PlusDi:F0} — 順勢做空";
        }

        return new Signal
        {
            SignalId = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
            Action = action, Confidence = Math.Round(confidence, 2), Reason = reason,
            Interval = config.Interval,
            Indicators = new() { ["adx"] = adx.Adx, ["plus_di"] = adx.PlusDi, ["minus_di"] = adx.MinusDi, ["price"] = Math.Round(bars[^1].Close, 4) },
        };
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16], Strategy = "di_trend_ls",
        Symbol = c.Symbol, Exchange = c.Exchange, Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
