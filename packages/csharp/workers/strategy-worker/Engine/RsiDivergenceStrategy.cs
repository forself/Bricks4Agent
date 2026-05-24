using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// RSI 背離 —— 價創新低但 RSI 沒創新低(底背離)→ 買;價創新高但 RSI 沒創新高(頂背離)→ 賣。
/// 看的是「價與動量的不一致」,跟 rsi 超賣是不同維度的反轉訊號。
///   buy:  現價 < 近 20 bar 前低、但現 RSI > 前低 RSI、且 RSI < 45(偏弱區)
///   sell: 現價 > 近 20 bar 前高、但現 RSI < 前高 RSI、且 RSI > 55(偏強區)
/// 2026-05-25 新增、先 paper 驗證。
/// </summary>
public class RsiDivergenceStrategy : IStrategy
{
    public string Name => "rsi_divergence";
    public string Description => "RSI 背離 — 價創新低 RSI 不創低(底背離)買、價創新高 RSI 不創高(頂背離)賣";
    public StrategyCategory Category => StrategyCategory.Momentum;
    public int MinBars => 50;
    public decimal MinCapitalUsdt => 100m;

    private static decimal Rsi(List<BarData> bars, int endIdx, int p)
    {
        decimal gain = 0, loss = 0;
        for (int i = endIdx - p + 1; i <= endIdx; i++)
        {
            var ch = bars[i].Close - bars[i - 1].Close;
            if (ch > 0) gain += ch; else loss -= ch;
        }
        if (loss == 0) return 100m;
        var rs = (gain / p) / (loss / p);
        return 100m - 100m / (1m + rs);
    }

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        if (bars.Count < MinBars) return Hold(config, $"need ≥{MinBars} bars");
        int n = bars.Count, look = 20, period = 14;
        decimal close = bars[^1].Close;
        decimal rsiNow = Rsi(bars, n - 1, period);

        int loIdx = n - look, hiIdx = n - look;     // 近 [n-look, n-3] 的 price pivot
        for (int i = n - look; i <= n - 3; i++)
        {
            if (bars[i].Close < bars[loIdx].Close) loIdx = i;
            if (bars[i].Close > bars[hiIdx].Close) hiIdx = i;
        }
        decimal rsiLo = Rsi(bars, loIdx, period), rsiHi = Rsi(bars, hiIdx, period);

        string action = "hold"; decimal conf = 0m; string reason;
        if (close < bars[loIdx].Close && rsiNow > rsiLo && rsiNow < 45m)
        {
            action = "buy"; conf = 0.7m;
            reason = $"底背離:價 {close:F4}<前低 {bars[loIdx].Close:F4} 但 RSI {rsiNow:F0}>前低 {rsiLo:F0}";
        }
        else if (close > bars[hiIdx].Close && rsiNow < rsiHi && rsiNow > 55m)
        {
            action = "sell"; conf = 0.7m;
            reason = $"頂背離:價 {close:F4}>前高 {bars[hiIdx].Close:F4} 但 RSI {rsiNow:F0}<前高 {rsiHi:F0}";
        }
        else reason = $"無背離(RSI {rsiNow:F0})";

        return Sig(config, action, conf, reason, new()
        {
            ["price"] = Math.Round(close, 4), ["rsi"] = Math.Round(rsiNow, 1),
            ["prior_low"] = Math.Round(bars[loIdx].Close, 4), ["prior_high"] = Math.Round(bars[hiIdx].Close, 4),
        });
    }

    private Signal Hold(StrategyConfig c, string r) => Sig(c, "hold", 0m, r, new());
    private Signal Sig(StrategyConfig c, string a, decimal conf, string r, Dictionary<string, decimal> ind) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16], Strategy = Name, Symbol = c.Symbol, Exchange = c.Exchange,
        Action = a, Confidence = conf, Reason = r, Interval = c.Interval, Indicators = ind,
    };
}
