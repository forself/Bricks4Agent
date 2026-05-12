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

    /// <summary>
    /// 開倉當下的策略名稱（AutoTrader 開倉時填、手動下單通常為 null）。
    /// FillPoller 補抓 perp realized_pnl 時會 best-effort 用同 symbol 最近一筆有 strategy 的 row 繼承。
    /// </summary>
    public string? Strategy { get; set; }
}
