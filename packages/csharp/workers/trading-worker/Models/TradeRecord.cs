namespace TradingWorker.Models;

/// <summary>
/// 成交紀錄（已完成的交易）。
/// </summary>
public class TradeRecord
{
    public string TradeId    { get; set; } = string.Empty;
    public string OrderId    { get; set; } = string.Empty;
    public string Symbol     { get; set; } = string.Empty;
    public string Exchange   { get; set; } = string.Empty;
    public string Side       { get; set; } = string.Empty;
    public decimal Quantity  { get; set; }
    public decimal Price     { get; set; }
    public decimal? Fee      { get; set; }
    public decimal? RealizedPnl { get; set; }
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
}
