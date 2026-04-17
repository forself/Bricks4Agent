namespace TradingWorker.Models;

/// <summary>
/// 交易帳戶摘要。
/// </summary>
public class TradingAccount
{
    public string Exchange       { get; set; } = string.Empty;
    public string AccountId      { get; set; } = string.Empty;
    public decimal Cash          { get; set; }
    public decimal PortfolioValue { get; set; }
    public decimal BuyingPower   { get; set; }
    public decimal DayPnl        { get; set; }
    public decimal TotalPnl      { get; set; }
    public string Currency       { get; set; } = "USD";
    public bool IsPaper          { get; set; } = true;
    public DateTime UpdatedAt    { get; set; } = DateTime.UtcNow;
}
