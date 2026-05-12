using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// OBV 能量潮趨勢確認策略。
///
/// 訊號規則：
///   OBV 剛上穿 EMA（加速流入）→ buy strong（conf 0.7）
///   OBV &gt; SMA → buy weak（資金流入）
///   OBV &lt; SMA → sell weak（資金流出）
///
/// 設計對標：朋友 ai-quant-starter2/strategy_selector.py:s_obv_trend。
/// </summary>
public class ObvStrategy : IStrategy
{
    public string Name => "obv";
    public string Description => "OBV 能量潮 — 累積成交量配對價格漲跌、確認資金流向";
    public StrategyCategory Category => StrategyCategory.Volume;
    public int MinBars => 30;
    public decimal MinCapitalUsdt => 50m;

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        var o = Obv.Compute(bars);
        if (o == null) return Hold(config, "Not enough data for OBV");

        string action = "hold"; decimal conf = 0.5m; string reason;
        if (o.JustCrossedAboveEma)
        {
            action = "buy"; conf = 0.7m;
            reason = $"OBV 剛上穿 EMA（OBV={o.Obv} → EMA={o.Ema}）— 加速流入";
        }
        else if (o.AboveSma)
        {
            action = "buy"; conf = 0.55m;
            reason = $"OBV={o.Obv} &gt; SMA={o.Sma} — 資金流入";
        }
        else
        {
            action = "sell"; conf = 0.55m;
            reason = $"OBV={o.Obv} &lt; SMA={o.Sma} — 資金流出";
        }

        return new Signal
        {
            SignalId = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
            Action = action, Confidence = Math.Round(conf, 2), Reason = reason, Interval = config.Interval,
            Indicators = new()
            {
                ["price"] = Math.Round(bars[^1].Close, 4),
                ["obv"] = o.Obv, ["obv_sma"] = o.Sma, ["obv_ema"] = o.Ema,
                ["above_sma"] = o.AboveSma ? 1m : 0m,
                ["just_crossed_above_ema"] = o.JustCrossedAboveEma ? 1m : 0m,
            },
        };
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = "obv", Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
