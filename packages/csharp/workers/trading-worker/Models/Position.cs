namespace TradingWorker.Models;

/// <summary>
/// 持倉。
/// </summary>
public class Position
{
    public string Symbol        { get; set; } = string.Empty;
    public string Exchange      { get; set; } = string.Empty;
    public decimal Quantity     { get; set; }
    public decimal AvgEntryPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal MarketValue  { get; set; }
    public decimal UnrealizedPnl { get; set; }
    public decimal UnrealizedPnlPercent { get; set; }
    public string Side          { get; set; } = "long"; // "long" | "short"
    public DateTime UpdatedAt   { get; set; } = DateTime.UtcNow;
}
