using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// Donchian Channel 突破策略（Turtle 系統的核心邏輯）。
///
/// 訊號規則：
///   收盤 ≥ 前 N 根高點 → buy strong（突破上軌）
///   收盤 ≤ 前 N 根低點 → sell strong（跌破下軌）
///   收盤 &gt; mid 但未破上軌 → buy weak
///   收盤 &lt; mid 但未破下軌 → sell weak
///
/// 註：用「前 N 根」即不含當前 K，避免當前 K 自體比較永遠等於 max。
///
/// 設計對標：朋友 ai-quant-starter2/strategy_selector.py:s_donchian。
/// </summary>
public class DonchianStrategy : IStrategy
{
    public string Name => "donchian";
    public string Description => "Donchian Channel — 經典 Turtle 突破系統（N 根高低突破即跟勢）";
    public StrategyCategory Category => StrategyCategory.Breakout;
    public int MinBars => 25;
    public decimal MinCapitalUsdt => 100m;

    private const int Period = 20;

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["period"] = new() { Type = "int", Default = 20, Min = 10, Max = 55, Step = 5, Description = "通道週期" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        var d = Donchian.Compute(bars, Period);
        if (d == null) return Hold(config, "Not enough data for Donchian");

        var price = bars[^1].Close;
        string action = "hold"; decimal conf = 0.5m; string reason;

        if (price >= d.PrevUpper)
        {
            action = "buy"; conf = 0.75m;
            reason = $"突破唐奇安上軌（前 {Period} 根高點 {d.PrevUpper}）、現價 {price} 🚀";
        }
        else if (price <= d.PrevLower)
        {
            action = "sell"; conf = 0.75m;
            reason = $"跌破唐奇安下軌（前 {Period} 根低點 {d.PrevLower}）、現價 {price} 📉";
        }
        else if (price > d.Mid)
        {
            action = "buy"; conf = 0.55m;
            reason = $"通道中線 {d.Mid} 之上、現價 {price} — 弱多";
        }
        else
        {
            action = "sell"; conf = 0.55m;
            reason = $"通道中線 {d.Mid} 之下、現價 {price} — 弱空";
        }

        if (d.ChannelWidthPct < 5m) reason += $"（通道收窄 {d.ChannelWidthPct:F2}%、突破蓄能）";

        return Build(config, action, conf, reason, new()
        {
            ["price"] = Math.Round(price, 4),
            ["upper"] = d.Upper, ["mid"] = d.Mid, ["lower"] = d.Lower,
            ["channel_width_pct"] = d.ChannelWidthPct,
        });
    }

    private static Signal Build(StrategyConfig c, string action, decimal conf, string reason, Dictionary<string, decimal> indicators) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = "donchian", Symbol = c.Symbol, Exchange = c.Exchange,
        Action = action, Confidence = Math.Round(conf, 2), Reason = reason, Interval = c.Interval,
        Indicators = indicators,
    };

    private static Signal Hold(StrategyConfig c, string reason) =>
        Build(c, "hold", 0m, reason, new());
}
