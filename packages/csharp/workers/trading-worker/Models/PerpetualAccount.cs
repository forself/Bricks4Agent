namespace TradingWorker.Models;

/// <summary>
/// 永續合約帳戶總覽。
///
/// Equity = Balance + UnrealizedPnl
/// AvailableMargin = Equity - MarginUsed（可開新倉的額度）
/// </summary>
public class PerpetualAccount
{
    public string Exchange       { get; set; } = string.Empty;
    public string AccountId      { get; set; } = string.Empty;
    public string Currency       { get; set; } = "USDT";

    public decimal Balance       { get; set; }   // 帳戶現金（USDT）
    public decimal Equity        { get; set; }   // Balance + 未實現損益
    public decimal UnrealizedPnl { get; set; }
    public decimal MarginUsed    { get; set; }   // 所有開倉佔用保證金
    public decimal AvailableMargin { get; set; }
    public int OpenPositionsCount { get; set; }

    public bool IsDemo           { get; set; }   // BingX VST demo / live
    public DateTime UpdatedAt    { get; set; } = DateTime.UtcNow;
}
