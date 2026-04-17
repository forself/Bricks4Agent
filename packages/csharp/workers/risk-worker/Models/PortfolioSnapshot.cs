namespace RiskWorker.Models;

/// <summary>
/// 投組快照 — 風控引擎用來判斷當前曝險。
/// 由呼叫端從 trading-worker 查詢後傳入。
/// </summary>
public class PortfolioSnapshot
{
    public decimal Cash           { get; set; }
    public decimal PortfolioValue { get; set; }
    public decimal DayPnl         { get; set; }
    public decimal TotalPnl       { get; set; }
    public decimal PeakValue      { get; set; }  // 歷史最高淨值（用於計算回撤）
    public int DailyTradeCount    { get; set; }
    public List<PositionEntry> Positions { get; set; } = new();
}

public class PositionEntry
{
    public string Symbol        { get; set; } = string.Empty;
    public string Exchange      { get; set; } = string.Empty;
    public decimal Quantity     { get; set; }
    public decimal MarketValue  { get; set; }
    public decimal UnrealizedPnl { get; set; }
}
