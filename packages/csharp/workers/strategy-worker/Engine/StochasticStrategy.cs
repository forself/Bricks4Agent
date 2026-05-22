using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// RSI + Stochastic 雙重共振策略。
///
/// 朋友 repo 的 rsi_stoch 設計：兩個 oscillator 都站在同向才出強訊號、
/// 任一單獨偏離只出弱訊號。
///
/// 訊號規則：
///   RSI < 30 + %K < 20 → buy strong（雙重超賣、conf 0.8）
///   RSI < 40 單獨偏低  → buy weak（conf 0.55）
///   RSI > 70 + %K > 80 → sell strong
///   RSI > 60 單獨偏高  → sell weak
///   其餘                → hold
///
/// 設計對標：朋友 ai-quant-starter2/app/services/strategy_selector.py:s_rsi_stoch。
/// </summary>
public class StochasticStrategy : IStrategy
{
    public string Name => "rsi_stoch";
    public string Description => "RSI + Stochastic 雙重共振 — 兩個 oscillator 同向時出強訊號";
    public StrategyCategory Category => StrategyCategory.MeanReversion;
    public int MinBars => 18;   // RSI 14 + Stoch K 14 + Stoch D 3 - 1 overlap
    public decimal MinCapitalUsdt => 50m;

    private const int RsiPeriod = 14;
    private const int StochK = 14;
    private const int StochD = 3;

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["rsi_period"] = new() { Type = "int", Default = RsiPeriod, Min = 7,  Max = 21, Step = 7, Description = "RSI 週期" },
        ["stoch_k"]    = new() { Type = "int", Default = StochK,    Min = 7,  Max = 21, Step = 7, Description = "Stochastic %K 週期" },
        ["stoch_d"]    = new() { Type = "int", Default = StochD,    Min = 3,  Max = 5,  Step = 1, Description = "Stochastic %D 平滑" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        if (bars.Count < MinBars) return Hold(config, "Not enough data");

        int rsiPeriod = config.GetParam("rsi_period", RsiPeriod);
        int stochK    = config.GetParam("stoch_k", StochK);
        int stochD    = config.GetParam("stoch_d", StochD);

        var rsi = CalcRsi(bars, rsiPeriod);
        var stoch = Stochastic.Compute(bars, stochK, stochD);
        if (stoch == null) return Hold(config, "Stoch compute failed");

        string action = "hold";
        decimal confidence = 0.5m;
        string reason;

        if (rsi < 30m && stoch.K < 20m)
        {
            action = "buy";
            confidence = 0.8m;
            reason = $"雙重超賣：RSI={rsi:F1} %K={stoch.K:F1}";
        }
        else if (rsi < 40m)
        {
            action = "buy";
            confidence = 0.55m;
            reason = $"RSI={rsi:F1} 單獨偏低（%K={stoch.K:F1}）— 弱買訊";
        }
        else if (rsi > 70m && stoch.K > 80m)
        {
            action = "sell";
            confidence = 0.8m;
            reason = $"雙重超買：RSI={rsi:F1} %K={stoch.K:F1}";
        }
        else if (rsi > 60m)
        {
            action = "sell";
            confidence = 0.55m;
            reason = $"RSI={rsi:F1} 單獨偏高（%K={stoch.K:F1}）— 弱賣訊";
        }
        else
        {
            reason = $"RSI={rsi:F1} %K={stoch.K:F1} 中性區 — 無訊號";
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
                ["rsi"]       = Math.Round(rsi, 4),
                ["stoch_k"]   = stoch.K,
                ["stoch_d"]   = stoch.D,
                ["price"]     = Math.Round(bars[^1].Close, 4),
            },
        };
    }

    private static decimal CalcRsi(List<BarData> bars, int period)
    {
        decimal gains = 0m, losses = 0m;
        for (int i = bars.Count - period; i < bars.Count; i++)
        {
            var diff = bars[i].Close - bars[i - 1].Close;
            if (diff > 0) gains += diff;
            else losses -= diff;
        }
        var avgGain = gains / period;
        var avgLoss = losses / period;
        if (avgLoss == 0m) return 100m;
        var rs = avgGain / avgLoss;
        return 100m - (100m / (1m + rs));
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = "rsi_stoch",
        Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
