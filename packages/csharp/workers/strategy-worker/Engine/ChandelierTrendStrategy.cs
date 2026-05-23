using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// Donchian 通道突破(多空反手 / Turtle 式)—— 收盤突破前 N 根高點做多、跌破前 N 根低點做空,
/// 通道內維持。經典趨勢跟隨骨幹:賺大波段、在區間裡不動。用 ATR 當「雜訊過濾」:突破幅度
/// 必須 ≥ atrFilter×ATR 才算數,濾掉貼著前高/前低的假突破。
///
///   priorHigh/priorLow = 前 N 根(不含當根)高/低點
///   close > priorHigh + atrFilter×ATR → buy(向上突破做多)
///   close < priorLow  − atrFilter×ATR → sell(向下突破做空)
///   其餘 → hold
///
/// 多空對稱:在 LongShortBacktestEngine 是「多空反手」、在 long-only 引擎 sell=平倉。
/// 無 lookahead:突破基準取前 N 根(不含當根);ATR 回看。
/// </summary>
public class ChandelierTrendStrategy : IStrategy
{
    public string Name => "chandelier_trend";
    public string Description => "Donchian 通道突破(多空反手)— 破前高做多/破前低做空,ATR 過濾假突破";
    public StrategyCategory Category => StrategyCategory.Breakout;
    public int MinBars => 60;
    public decimal MinCapitalUsdt => 120m;

    private const int Lookback   = 20;    // 突破通道回看(前 N 根高/低)
    private const int AtrPeriod  = 22;
    private const decimal AtrFilter = 0.25m; // 突破需 ≥ atrFilter×ATR、濾雜訊

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["ch_lookback"]   = new() { Type = "int",     Default = Lookback,  Min = 10,   Max = 55,   Step = 5,   Description = "突破通道回看根數" },
        ["ch_atr_period"] = new() { Type = "int",     Default = AtrPeriod, Min = 10,   Max = 30,   Step = 2,   Description = "ATR 週期" },
        ["ch_atr_filter"] = new() { Type = "decimal", Default = AtrFilter, Min = 0m,   Max = 1.0m, Step = 0.05m, Description = "突破最小幅度(ATR 倍數)、濾假突破" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        int lb = config.GetParam("ch_lookback", Lookback);
        int atrP = config.GetParam("ch_atr_period", AtrPeriod);
        decimal atrFilter = config.GetParam("ch_atr_filter", AtrFilter);

        int need = Math.Max(lb, atrP) + 2;
        if (bars.Count < need) return Hold(config, $"資料不足(需 {need}+ 根)");

        int end = bars.Count - 1;
        decimal close = bars[end].Close;

        decimal priorHigh = decimal.MinValue, priorLow = decimal.MaxValue;
        for (int i = end - lb; i < end; i++)
        {
            if (bars[i].High > priorHigh) priorHigh = bars[i].High;
            if (bars[i].Low  < priorLow)  priorLow  = bars[i].Low;
        }
        decimal atr = Atr(bars, atrP);
        decimal upTrig = priorHigh + atrFilter * atr;
        decimal dnTrig = priorLow - atrFilter * atr;

        string action; decimal confidence; string reason;
        if (close > upTrig)
        {
            action = "buy";
            decimal over = atr > 0 ? (close - priorHigh) / atr : 1m;
            confidence = Math.Clamp(0.6m + over * 0.1m, 0.6m, 0.95m);
            reason = $"close {close:F2} 突破前 {lb} 根高 {priorHigh:F2}(+{atrFilter}×ATR)— 做多";
        }
        else if (close < dnTrig)
        {
            action = "sell";
            decimal over = atr > 0 ? (priorLow - close) / atr : 1m;
            confidence = Math.Clamp(0.6m + over * 0.1m, 0.6m, 0.95m);
            reason = $"close {close:F2} 跌破前 {lb} 根低 {priorLow:F2}(−{atrFilter}×ATR)— 做空";
        }
        else
        {
            action = "hold";
            confidence = 0.5m;
            reason = $"close {close:F2} 在通道 [{priorLow:F2}, {priorHigh:F2}] 內 — 等突破、維持";
        }

        return new Signal
        {
            SignalId = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
            Action = action, Confidence = Math.Round(confidence, 2), Reason = reason,
            Interval = config.Interval,
            Indicators = new()
            {
                ["prior_high"] = Math.Round(priorHigh, 4),
                ["prior_low"]  = Math.Round(priorLow, 4),
                ["atr"]        = Math.Round(atr, 4),
                ["price"]      = Math.Round(close, 4),
            },
        };
    }

    /// <summary>簡單移動平均 ATR(近 period 根 TR);只用回看資料、無 lookahead。</summary>
    private static decimal Atr(List<BarData> bars, int period)
    {
        decimal sum = 0m;
        for (int i = bars.Count - period; i < bars.Count; i++)
        {
            var b = bars[i]; var pc = bars[i - 1].Close;
            sum += Math.Max(b.High - b.Low, Math.Max(Math.Abs(b.High - pc), Math.Abs(b.Low - pc)));
        }
        return sum / period;
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = "chandelier_trend", Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
