using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 複合策略 — 同時跑多個策略，用加權投票決定最終訊號。
/// </summary>
public class CompositeStrategy : IStrategy
{
    public string Name => "composite";

    private readonly List<(IStrategy Strategy, decimal Weight)> _strategies;

    public CompositeStrategy(List<(IStrategy, decimal)> strategies)
    {
        _strategies = strategies;
    }

    /// <summary>預設組合：SMA Cross + RSI + MACD，等權重。</summary>
    public static CompositeStrategy Default() => new(new List<(IStrategy, decimal)>
    {
        (new SmaCrossStrategy(), 1.0m),
        (new RsiStrategy(),      1.0m),
        (new MacdStrategy(),     1.0m),
    });

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        var signals = _strategies
            .Select(s => (Signal: s.Strategy.Evaluate(bars, config), s.Weight))
            .ToList();

        // 加權投票——hold 視為棄權、不參與比較。原本作法把 hold@conf=0.5 也納入 score、
        // 結果只要任一策略中性、buy/sell 票就被稀釋（3 個策略中 2 個中性、1 個 buy 也會輸給
        // hold 的 0.33）。改成只看 buy vs sell 的累積 score，用「同向票權重總和」當分母算
        // 平均信心；全員 hold → hold @ 低信心；buy/sell 持平 → hold。
        decimal buyScore = 0, sellScore = 0;
        decimal buyWeight = 0, sellWeight = 0;
        var allIndicators = new Dictionary<string, decimal>();

        foreach (var (signal, weight) in signals)
        {
            switch (signal.Action)
            {
                case "buy":
                    buyScore  += signal.Confidence * weight;
                    buyWeight += weight;
                    break;
                case "sell":
                    sellScore  += signal.Confidence * weight;
                    sellWeight += weight;
                    break;
                // hold: 不算 score、不算 weight（棄權）
            }

            foreach (var kv in signal.Indicators)
                allIndicators[$"{signal.Strategy}.{kv.Key}"] = kv.Value;
        }

        string action;
        decimal confidence;
        if (buyScore > sellScore && buyWeight > 0)
        {
            action = "buy";
            confidence = buyScore / buyWeight;   // 同向票的平均信心
        }
        else if (sellScore > buyScore && sellWeight > 0)
        {
            action = "sell";
            confidence = sellScore / sellWeight;
        }
        else
        {
            // 全員 hold 或 buy/sell 持平 → 不出單
            action = "hold";
            confidence = 0.3m;
        }

        var reasons = signals
            .Select(s => $"[{s.Signal.Strategy}] {s.Signal.Action}({s.Signal.Confidence:P0}): {s.Signal.Reason}")
            .ToList();

        return new Signal
        {
            SignalId   = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy   = Name,
            Symbol     = config.Symbol,
            Exchange   = config.Exchange,
            Action     = action,
            Confidence = Math.Round(confidence, 2),
            Reason     = string.Join(" | ", reasons),
            Interval   = config.Interval,
            Indicators = allIndicators,
        };
    }
}
