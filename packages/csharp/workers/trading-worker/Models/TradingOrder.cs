namespace TradingWorker.Models;

/// <summary>
/// 交易訂單。
/// </summary>
public class TradingOrder
{
    public string OrderId      { get; set; } = string.Empty;
    public string Symbol       { get; set; } = string.Empty;
    public string Exchange     { get; set; } = string.Empty; // "alpaca" | "binance"
    public string Side         { get; set; } = string.Empty; // "buy" | "sell"
    public string OrderType    { get; set; } = "market";     // "market" | "limit" | "stop" | "stop_limit"
    public decimal Quantity    { get; set; }
    public decimal? LimitPrice { get; set; }
    public decimal? StopPrice  { get; set; }
    public string TimeInForce  { get; set; } = "gtc";        // "gtc" | "day" | "ioc" | "fok"
    public string Status       { get; set; } = "pending";    // "pending" | "submitted" | "filled" | "partial" | "cancelled" | "rejected"
    public decimal FilledQty   { get; set; }
    public decimal? FilledPrice { get; set; }
    public string? ExternalId  { get; set; }                  // 交易所回傳的訂單 ID
    public string? Error       { get; set; }
    public DateTime CreatedAt  { get; set; } = DateTime.UtcNow;
    public DateTime? FilledAt  { get; set; }
    public DateTime UpdatedAt  { get; set; } = DateTime.UtcNow;
}
