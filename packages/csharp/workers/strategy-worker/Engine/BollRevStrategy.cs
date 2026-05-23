using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 波動率均值回歸(Bollinger 下軌反彈)。
///   進場(buy):收盤 &lt; 布林下軌(SMA20 − 2×std20)且 收盤 &gt; SMA200(多頭大勢)
///   出場(sell):收盤 ≥ 中軌(SMA20)
///   其餘 hold
///
/// 2026-05-24 經 8 年深日線驗證:跨時段一致 ~65%、1× 報酬正、跟趨勢腿去相關(|r|~0.0)、
/// 跟 rsi2_rev 中度相關(~0.42,同為均值回歸)。組合裡 rsi2_rev / boll_rev 擇一或都留(知道偏像)。
/// 必須用 ~1× 有效槓桿;高有效槓桿在 crypto 必強平。
/// </summary>
public class BollRevStrategy : IStrategy
{
    public string Name => "boll_rev";
    public string Description => "波動率均值回歸 — 跌破布林下軌(20,2) + SMA200 上方買、收回中軌出";
    public StrategyCategory Category => StrategyCategory.MeanReversion;
    public int MinBars => 205;
    public decimal MinCapitalUsdt => 100m;

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        if (bars.Count < MinBars) return Hold(config, $"need ≥{MinBars} bars for boll_rev");
        int n = bars.Count;
        decimal close = bars[^1].Close;
        decimal sma20 = 0; for (int k = n - 20; k < n; k++) sma20 += bars[k].Close; sma20 /= 20m;
        decimal var20 = 0; for (int k = n - 20; k < n; k++) var20 += (bars[k].Close - sma20) * (bars[k].Close - sma20);
        decimal std20 = (decimal)Math.Sqrt((double)(var20 / 20m));
        decimal lower = sma20 - 2m * std20;
        decimal sma200 = 0; for (int k = n - 200; k < n; k++) sma200 += bars[k].Close; sma200 /= 200m;

        string action = "hold"; decimal conf = 0m; string reason;
        if (close < lower && close > sma200)
        {
            action = "buy"; conf = 0.7m;
            reason = $"收盤 {close:F4} < 布林下軌 {lower:F4} 且 > SMA200 {sma200:F4} — 多頭超跌買";
        }
        else if (close >= sma20)
        {
            action = "sell"; conf = 0.7m;
            reason = $"收盤 {close:F4} ≥ 中軌 {sma20:F4} — 回均出場";
        }
        else reason = $"下軌 {lower:F4} < {close:F4} < 中軌 {sma20:F4} — 維持";

        return new Signal
        {
            SignalId = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
            Action = action, Confidence = conf, Reason = reason, Interval = config.Interval,
            Indicators = new()
            {
                ["price"] = Math.Round(close, 4), ["bb_lower"] = Math.Round(lower, 4),
                ["bb_mid"] = Math.Round(sma20, 4), ["sma200"] = Math.Round(sma200, 4),
            },
        };
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16], Strategy = "boll_rev",
        Symbol = c.Symbol, Exchange = c.Exchange, Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
