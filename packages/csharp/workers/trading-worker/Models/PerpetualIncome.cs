namespace TradingWorker.Models;

/// <summary>
/// 永續合約「收入流水」單筆紀錄——從交易所 income/funding history endpoint 取。
/// 主要用途：捕捉平倉產生的 REALIZED_PNL、補進 trades 表給 DailyReport 算實際盈虧。
///
/// IncomeType 統一小寫：realized_pnl / commission / funding_fee / transfer / other。
/// </summary>
public class PerpetualIncome
{
    public string Symbol     { get; set; } = "";
    public string Exchange   { get; set; } = "";
    public string IncomeType { get; set; } = "";   // realized_pnl / commission / funding_fee / transfer / other
    public decimal Income    { get; set; }
    public string Asset      { get; set; } = "USDT";
    public string? TradeId   { get; set; }          // 對應交易所成交 ID（同一筆 close 可能拆 commission + realized_pnl 兩筆）
    public string? TranId    { get; set; }          // 交易所流水 ID（idempotency）
    public DateTime Time     { get; set; }
}
