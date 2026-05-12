using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// VWAP 位置策略——以機構成本均線作多空確認。
///
/// 朋友 repo 的 VWAP 解讀是「順勢確認」非均值回歸：
///   價格遠高於 VWAP → 機構在賺、可能繼續推
///   價格遠低於 VWAP → 機構在虧、可能繼續壓
///
/// 訊號規則：
///   dev > +2% → buy（價格站穩 VWAP 上方、conf 0.7）
///   dev > 0   → buy weak（conf 0.55）
///   dev < -2% → sell（conf 0.7）
///   dev < 0   → sell weak（conf 0.55）
///   |dev| 很小（< 0.5%） → hold（黏在 VWAP、方向不明）
///
/// 設計對標：朋友 ai-quant-starter2/app/services/strategy_selector.py:s_vwap。
/// </summary>
public class VwapStrategy : IStrategy
{
    public string Name => "vwap";
    public string Description => "VWAP 位置 — 用機構成本均線確認多空方向";
    public StrategyCategory Category => StrategyCategory.Trend;
    public int MinBars => 20;
    public decimal MinCapitalUsdt => 50m;

    private const int Period = 20;
    private const decimal StrongPct = 2m;
    private const decimal NeutralPct = 0.5m;

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["period"] = new() { Type = "int", Default = 20, Min = 10, Max = 50, Step = 5, Description = "VWAP rolling 視窗" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        var r = Vwap.Compute(bars, Period);
        if (r == null) return Hold(config, "Not enough data for VWAP");

        var price = bars[^1].Close;
        var dev = r.DeviationPct;
        string action = "hold";
        decimal confidence = 0.5m;
        string reason;

        if (Math.Abs(dev) < NeutralPct)
        {
            reason = $"價 {price} 緊貼 VWAP {r.Value}（偏離 {dev:F2}%）— 方向不明";
        }
        else if (dev >= StrongPct)
        {
            action = "buy";
            confidence = 0.7m;
            reason = $"價 {price} 高於 VWAP {r.Value} +{dev:F2}% — 機構偏多";
        }
        else if (dev > 0m)
        {
            action = "buy";
            confidence = 0.55m;
            reason = $"價 {price} 略高 VWAP {r.Value} +{dev:F2}% — 弱多";
        }
        else if (dev <= -StrongPct)
        {
            action = "sell";
            confidence = 0.7m;
            reason = $"價 {price} 低於 VWAP {r.Value} {dev:F2}% — 機構偏空";
        }
        else
        {
            action = "sell";
            confidence = 0.55m;
            reason = $"價 {price} 略低 VWAP {r.Value} {dev:F2}% — 弱空";
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
                ["price"]         = Math.Round(price, 4),
                ["vwap"]          = r.Value,
                ["deviation_pct"] = dev,
            },
        };
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = "vwap",
        Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
