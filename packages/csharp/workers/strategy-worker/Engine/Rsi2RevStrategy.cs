using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 短期均值回歸(Connors RSI-2 風格)。
///   進場(buy):RSI(2) &lt; 10(深度超賣)且 收盤 &gt; SMA200(只在多頭大勢買回檔)
///   出場(sell):收盤 &gt; SMA5(短均線上方 = 反彈完成)
///   其餘 hold
///
/// 2026-05-24 經 8 年深日線驗證:跨時段一致 ~65%、1× 報酬正、跟趨勢策略去相關(|r|~0.1)
/// → 適合當組合的「均值回歸腿」。持倉短(通常數根 K)。
/// 必須用 ~1× 有效槓桿;高有效槓桿在 crypto 必強平。
/// </summary>
public class Rsi2RevStrategy : IStrategy
{
    public string Name => "rsi2_rev";
    public string Description => "短期均值回歸 — RSI(2)<10 超賣 + SMA200 上方買、收回 SMA5 上方出";
    public StrategyCategory Category => StrategyCategory.MeanReversion;
    public int MinBars => 205;                  // SMA200 + 緩衝
    public decimal MinCapitalUsdt => 100m;

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        if (bars.Count < MinBars) return Hold(config, $"need ≥{MinBars} bars for rsi2_rev");
        int n = bars.Count;
        decimal close = bars[^1].Close;
        decimal sma200 = 0; for (int k = n - 200; k < n; k++) sma200 += bars[k].Close; sma200 /= 200m;
        decimal sma5 = 0; for (int k = n - 5; k < n; k++) sma5 += bars[k].Close; sma5 /= 5m;
        decimal rsi2 = Rsi(bars, 2);

        string action = "hold"; decimal conf = 0m; string reason;
        if (rsi2 < 10m && close > sma200)
        {
            action = "buy"; conf = 0.7m;
            reason = $"RSI(2)={rsi2:F1}<10 深度超賣 且收盤 {close:F4} > SMA200 {sma200:F4} — 多頭買回檔";
        }
        else if (close > sma5)
        {
            action = "sell"; conf = 0.7m;
            reason = $"收盤 {close:F4} > SMA5 {sma5:F4} — 反彈完成出場";
        }
        else reason = $"RSI(2)={rsi2:F1}、收盤 vs SMA5 {sma5:F4} — 維持";

        return new Signal
        {
            SignalId = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
            Action = action, Confidence = conf, Reason = reason, Interval = config.Interval,
            Indicators = new()
            {
                ["price"] = Math.Round(close, 4), ["rsi2"] = Math.Round(rsi2, 2),
                ["sma200"] = Math.Round(sma200, 4), ["sma5"] = Math.Round(sma5, 4),
            },
        };
    }

    private static decimal Rsi(List<BarData> b, int period)
    {
        int n = b.Count; if (n <= period + 1) return 50m;
        decimal ag = 0, al = 0;
        for (int i = 1; i < n; i++)
        {
            decimal ch = b[i].Close - b[i - 1].Close, g = Math.Max(0m, ch), l = Math.Max(0m, -ch);
            if (i <= period) { ag += g; al += l; if (i == period) { ag /= period; al /= period; } }
            else { ag = (ag * (period - 1) + g) / period; al = (al * (period - 1) + l) / period; }
        }
        return al == 0m ? 100m : 100m - 100m / (1m + ag / al);
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16], Strategy = "rsi2_rev",
        Symbol = c.Symbol, Exchange = c.Exchange, Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
