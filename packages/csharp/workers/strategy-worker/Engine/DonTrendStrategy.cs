using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 海龜式趨勢突破(Donchian breakout)。
///   進場(buy):收盤突破前 20 根最高 且 收盤 &gt; SMA100(順大勢)
///   出場(sell):收盤跌破前 10 根最低
///   其餘 hold(維持當前部位)
///
/// 2026-05-24 經 8 年深日線(2018-2026、跨多 régime)驗證:跨時段一致 ~57%、1× 有效槓桿報酬正、
/// 跟均值回歸策略(rsi2_rev / boll_rev)去相關(|r|~0.1)→ 適合當組合的「趨勢腿」。
/// 出場用 10 日低(無狀態)、不用進場後吊燈;真實停損/移動停損交給 AutoTrader 保護引擎。
/// 必須用 ~1× 有效槓桿(名目≈權益);高有效槓桿在 crypto 必強平。
/// </summary>
public class DonTrendStrategy : IStrategy
{
    public string Name => "don_trend";
    public string Description => "海龜趨勢突破 — 20 日高突破進場(SMA100 濾)、10 日低跌破出場";
    public StrategyCategory Category => StrategyCategory.Breakout;
    public int MinBars => 110;                  // SMA100 + 20 日通道
    public decimal MinCapitalUsdt => 100m;

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        if (bars.Count < MinBars) return Hold(config, $"need ≥{MinBars} bars for don_trend");
        int n = bars.Count;
        decimal close = bars[^1].Close;

        decimal sma100 = 0; for (int k = n - 100; k < n; k++) sma100 += bars[k].Close; sma100 /= 100m;
        decimal hh20 = 0m, ll10 = decimal.MaxValue;
        for (int k = n - 21; k < n - 1; k++) hh20 = Math.Max(hh20, bars[k].High);   // 前 20 根(不含當根)
        for (int k = n - 11; k < n - 1; k++) ll10 = Math.Min(ll10, bars[k].Low);    // 前 10 根

        string action = "hold"; decimal conf = 0m; string reason;
        if (close > hh20 && close > sma100)
        {
            action = "buy"; conf = 0.7m;
            reason = $"20 日高突破 {hh20:F4} 且收盤 {close:F4} > SMA100 {sma100:F4} — 順勢進場";
        }
        else if (close < ll10)
        {
            action = "sell"; conf = 0.7m;
            reason = $"收盤 {close:F4} 跌破 10 日低 {ll10:F4} — 趨勢失效出場";
        }
        else reason = $"區間內(10低 {ll10:F4} < {close:F4} < 20高 {hh20:F4})— 維持";

        return new Signal
        {
            SignalId = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
            Action = action, Confidence = conf, Reason = reason, Interval = config.Interval,
            Indicators = new()
            {
                ["price"] = Math.Round(close, 4), ["sma100"] = Math.Round(sma100, 4),
                ["hh20"] = Math.Round(hh20, 4), ["ll10"] = Math.Round(ll10, 4),
            },
        };
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16], Strategy = "don_trend",
        Symbol = c.Symbol, Exchange = c.Exchange, Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
