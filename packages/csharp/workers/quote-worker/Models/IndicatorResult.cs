namespace QuoteWorker.Models;

/// <summary>
/// 技術指標計算結果。
/// </summary>
public class IndicatorResult
{
    public string Symbol    { get; set; } = string.Empty;
    public string Indicator { get; set; } = string.Empty; // "SMA","EMA","RSI","MACD"
    public string Interval  { get; set; } = "1d";
    public int    Period    { get; set; }
    public DateTime Timestamp { get; set; }

    /// <summary>主要值（SMA/EMA/RSI 的單一值，MACD 的 MACD line）</summary>
    public decimal Value    { get; set; }

    /// <summary>MACD signal line（僅 MACD 使用）</summary>
    public decimal? Signal  { get; set; }

    /// <summary>MACD histogram（僅 MACD 使用）</summary>
    public decimal? Histogram { get; set; }

    /// <summary>完整序列（每個時間點的指標值）</summary>
    public List<TimestampedValue> Series { get; set; } = new();
}

public class TimestampedValue
{
    public DateTime Time  { get; set; }
    public decimal  Value { get; set; }
}
