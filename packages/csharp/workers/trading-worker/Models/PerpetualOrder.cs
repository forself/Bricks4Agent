namespace TradingWorker.Models;

/// <summary>
/// 永續合約訂單（BingX / 之後可能其他交易所）。
///
/// 跟 spot TradingOrder 的差別：
///   - PositionSide：long / short — 跟 Side 一起決定是 open / close 哪一邊
///   - Leverage：開倉用的槓桿倍數（BingX 是 per-symbol、開倉前先 SetLeverage）
///   - Side BUY/SELL 對應到 4 種 action：
///       BUY  + LONG  → open long
///       SELL + LONG  → close long
///       SELL + SHORT → open short
///       BUY  + SHORT → close short
///   - 故意不用 spot 的 TradingOrder，避免雙系統互相污染
/// </summary>
public class PerpetualOrder
{
    public string OrderId       { get; set; } = string.Empty;
    public string Symbol        { get; set; } = string.Empty;       // "BTC-USDT"
    public string Exchange      { get; set; } = string.Empty;       // "bingx"
    public string Side          { get; set; } = string.Empty;       // "buy" | "sell"
    public string PositionSide  { get; set; } = string.Empty;       // "long" | "short"
    public string OrderType     { get; set; } = "market";           // "market" | "limit" | "stop_market" | "take_profit_market"
    public decimal Quantity     { get; set; }                        // contracts (BingX 是 base 幣量、不是合約張數)
    public decimal? LimitPrice  { get; set; }
    public decimal? StopPrice   { get; set; }                        // for stop / take-profit orders
    public int Leverage         { get; set; } = 1;                   // 開倉時用的槓桿
    public string TimeInForce   { get; set; } = "gtc";               // "gtc" | "ioc" | "fok"
    public bool ReduceOnly      { get; set; } = false;               // 平倉用、不會反向開新倉
    public string Status        { get; set; } = "pending";
    public decimal FilledQty    { get; set; }
    public decimal? FilledPrice { get; set; }
    public string? ExternalId   { get; set; }
    public string? Error        { get; set; }
    public DateTime CreatedAt   { get; set; } = DateTime.UtcNow;
    public DateTime? FilledAt   { get; set; }
    public DateTime UpdatedAt   { get; set; } = DateTime.UtcNow;
}
