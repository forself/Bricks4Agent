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
    public string Interval    { get; set; } = "1d";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>策略使用的指標快照</summary>
    public Dictionary<string, decimal> Indicators { get; set; } = new();
}
