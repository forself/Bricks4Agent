using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// Keltner Channel 均值回歸策略。
///
/// 訊號規則（朋友 repo 解讀方式）：
///   收盤 &gt; Upper → sell（超買、預期回中軌）
///   收盤 &lt; Lower → buy（超賣、預期反彈）
///   通道內 + 中軌之上 → buy weak
///   通道內 + 中軌之下 → sell weak
///
/// 設計對標：朋友 ai-quant-starter2/strategy_selector.py:s_keltner。
/// </summary>
public class KeltnerStrategy : IStrategy
{
    public string Name => "keltner";
    public string Description => "Keltner Channel — EMA + ATR 通道、觸軌均值回歸";
    public StrategyCategory Category => StrategyCategory.MeanReversion;
    public int MinBars => 25;
    public decimal MinCapitalUsdt => 100m;

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        var k = Keltner.Compute(bars);
        if (k == null) return Hold(config, "Not enough data for Keltner");

        var price = bars[^1].Close;
        string action = "hold"; decimal conf = 0.5m; string reason;

        if (price > k.Upper)
        {
            action = "sell"; conf = 0.7m;
            reason = $"突破 Keltner 上軌 {k.Upper}（超買、預期回中軌 {k.Mid}）";
        }
        else if (price < k.Lower)
        {
            action = "buy"; conf = 0.7m;
            reason = $"跌破 Keltner 下軌 {k.Lower}（超賣、預期反彈到中軌 {k.Mid}）";
        }
        else if (price > k.Mid)
        {
            action = "buy"; conf = 0.55m;
            reason = $"通道內、中軌 {k.Mid} 之上 — 弱多";
        }
        else
        {
            action = "sell"; conf = 0.55m;
            reason = $"通道內、中軌 {k.Mid} 之下 — 弱空";
        }

        return new Signal
        {
            SignalId = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
            Action = action, Confidence = Math.Round(conf, 2), Reason = reason, Interval = config.Interval,
            Indicators = new()
            {
                ["price"] = Math.Round(price, 4),
                ["upper"] = k.Upper, ["mid"] = k.Mid, ["lower"] = k.Lower,
                ["channel_width_pct"] = k.ChannelWidthPct,
            },
        };
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = "keltner", Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
