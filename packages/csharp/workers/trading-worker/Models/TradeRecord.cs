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

    /// <summary>
    /// 多用戶:這筆成交屬於哪個 principal（隱私隔離用）。空 → storage 預設 admin "prn_dashboard"。
    /// 目前 FillPoller 走 env 預設帳戶=admin;朋友自有帳戶的 fill 歸屬待 FillPoller 多憑證化(Gap 2b)。
    /// </summary>
    public string? OwnerPrincipalId { get; set; }
}
