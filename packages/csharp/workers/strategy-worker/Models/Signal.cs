namespace StrategyWorker.Models;

/// <summary>
/// 交易訊號 — 策略引擎的輸出。
/// </summary>
public class Signal
{
    public string SignalId    { get; set; } = string.Empty;
    public string Strategy    { get; set; } = string.Empty; // "sma_cross", "rsi_oversold", "macd_divergence", "llm"
    public string Symbol      { get; set; } = string.Empty;
    public string Action      { get; set; } = "hold";       // "buy" | "sell" | "hold"
    public decimal Confidence { get; set; }                  // 0.0 ~ 1.0
    public string Reason      { get; set; } = string.Empty;  // 人類可讀的理由
    public string Exchange    { get; set; } = string.Empty;  // "alpaca" | "binance"
    public decimal? SuggestedQty   { get; set; }
    public decimal? SuggestedPrice { get; set; }

    /// <summary>選用停利目標。多單:後續 K 線 high 觸到此價即出場。null = 不啟用(回測引擎維持只靠反向訊號平倉)。</summary>
    public decimal? TargetPrice { get; set; }
    /// <summary>選用停損。多單:後續 K 線 low 觸到此價即出場。null = 不啟用。</summary>
    public decimal? StopPrice { get; set; }

    /// <summary>選用「部分出場」目標(scale-out)。多單:觸到此價先平掉 PartialExitFraction 比例、其餘續抱到 TargetPrice/StopPrice。
    /// null = 不啟用(整倉進出)。比 TargetPrice 近(沿途先落袋一半)。Phase 2:fib 1.13~1.272 反轉區平 50%。</summary>
    public decimal? PartialTargetPrice { get; set; }
    /// <summary>部分出場比例(0~1)。null/0 但有 PartialTargetPrice 時引擎用預設 0.5。</summary>
    public decimal? PartialExitFraction { get; set; }

    public string Interval    { get; set; } = "1d";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>策略使用的指標快照</summary>
    public Dictionary<string, decimal> Indicators { get; set; } = new();
}
