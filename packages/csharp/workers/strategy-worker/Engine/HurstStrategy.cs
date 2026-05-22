using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// Hurst 自適應策略 —— 用 Hurst 指數先判斷「市場性格」,再切換進場邏輯,與方向型策略正交。
///
/// 核心想法:大部分策略都假設「順勢 or 抄底」其中一種,但同一時間市場可能根本不適合那種玩法。
/// 先算 Hurst H(R/S rescaled range):
///   H ≥ trend_th(預設 0.55)→ 趨勢延續行情 → 動量模式:價 &gt; SMA20 買、&lt; SMA20 賣。
///   H ≤ meanrev_th(預設 0.45)→ 均值回歸行情 → 抄底/摸頭模式:RSI14 &lt; 35 買、&gt; 65 賣。
///   介於兩者(≈ 隨機漫步)→ hold,因為這時順勢和抄底都沒有統計優勢。
///
/// 跟既有策略的區別性:它不直接看方向,而是看「該不該順勢」這個 meta 維度,
/// 所以在 ensemble 裡能擋掉「趨勢策略在盤整裡亂進、回歸策略在單邊裡接刀」的盲區。
/// </summary>
public class HurstStrategy : IStrategy
{
    public string Name => "hurst_adaptive";
    public string Description => "Hurst Adaptive — 先用 Hurst 指數判斷趨勢/均值回歸,再切換動量或抄底進場";
    public StrategyCategory Category => StrategyCategory.Composite;
    public int MinBars => 60;
    public decimal MinCapitalUsdt => 150m;

    private const int Lookback = 100;
    private const decimal TrendTh = 0.55m;
    private const decimal MeanRevTh = 0.45m;
    private const int SmaPeriod = 20;
    private const int RsiPeriod = 14;
    private const decimal RsiBuyTh = 35m;
    private const decimal RsiSellTh = 65m;

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["hurst_lookback"]    = new() { Type = "int",     Default = 100,  Min = 48,   Max = 200,  Step = 16,   Description = "Hurst 取樣窗口" },
        ["hurst_trend_th"]    = new() { Type = "decimal", Default = 0.55m, Choices = new object[] { 0.52m, 0.55m, 0.58m, 0.6m }, Description = "判定趨勢的 H 門檻" },
        ["hurst_meanrev_th"]  = new() { Type = "decimal", Default = 0.45m, Choices = new object[] { 0.4m, 0.42m, 0.45m, 0.48m }, Description = "判定均值回歸的 H 門檻" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        if (bars.Count < MinBars) return Hold(config, $"Not enough data (need {MinBars}+ bars)");

        var lookback   = config.GetParam("hurst_lookback", Lookback);
        var trendTh    = config.GetParam("hurst_trend_th", TrendTh);
        var meanRevTh  = config.GetParam("hurst_meanrev_th", MeanRevTh);

        var h = Hurst.Compute(bars, lookback);
        if (h == null) return Hold(config, "Hurst 無法計算(資料不足)");
        decimal hv = h.Value;

        var price = bars[^1].Close;
        var sma   = CalcSma(bars, SmaPeriod);
        var rsi   = HarmonicPatterns.CalcRsiAt(bars, bars.Count - 1, RsiPeriod);

        string action = "hold";
        decimal confidence = 0.5m;
        string reason;
        decimal mode;  // 1 = 趨勢動量、-1 = 均值回歸、0 = 隨機漫步

        // H 偏離 0.5 越多 → 性格越鮮明 → 信心越高
        var conviction = Math.Clamp(0.5m + Math.Abs(hv - 0.5m), 0.5m, 0.9m);

        if (hv >= trendTh)
        {
            mode = 1m;
            if (price > sma)
            {
                action = "buy";
                confidence = conviction;
                reason = $"趨勢行情(H={hv:F3} ≥ {trendTh}) + 價 {price} > SMA{SmaPeriod} {sma:F2} → 順勢做多";
            }
            else if (price < sma)
            {
                action = "sell";
                confidence = conviction;
                reason = $"趨勢行情(H={hv:F3} ≥ {trendTh}) + 價 {price} < SMA{SmaPeriod} {sma:F2} → 順勢做空";
            }
            else
            {
                reason = $"趨勢行情(H={hv:F3}) 但價貼著 SMA{SmaPeriod} — 觀望";
            }
        }
        else if (hv <= meanRevTh)
        {
            mode = -1m;
            if (rsi < RsiBuyTh)
            {
                action = "buy";
                confidence = conviction;
                reason = $"均值回歸行情(H={hv:F3} ≤ {meanRevTh}) + RSI {rsi:F1} < {RsiBuyTh} → 抄底";
            }
            else if (rsi > RsiSellTh)
            {
                action = "sell";
                confidence = conviction;
                reason = $"均值回歸行情(H={hv:F3} ≤ {meanRevTh}) + RSI {rsi:F1} > {RsiSellTh} → 摸頭";
            }
            else
            {
                reason = $"均值回歸行情(H={hv:F3}) 但 RSI {rsi:F1} 未到極端 — 觀望";
            }
        }
        else
        {
            mode = 0m;
            reason = $"隨機漫步(H={hv:F3} 介於 {meanRevTh}~{trendTh}) — 順勢/抄底都無優勢、hold";
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
                ["price"]  = Math.Round(price, 4),
                ["hurst"]  = hv,
                ["sma20"]  = Math.Round(sma, 4),
                ["rsi14"]  = Math.Round(rsi, 2),
                ["mode"]   = mode,
            },
        };
    }

    private static decimal CalcSma(List<BarData> bars, int period)
    {
        if (bars.Count < period) return 0m;
        decimal sum = 0m;
        for (int i = bars.Count - period; i < bars.Count; i++) sum += bars[i].Close;
        return sum / period;
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = "hurst_adaptive",
        Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
