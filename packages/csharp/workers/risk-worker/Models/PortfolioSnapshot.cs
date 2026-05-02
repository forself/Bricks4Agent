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

    /// <summary>
    /// 每個 (exchange:symbol) 最近一次成交的 UTC 時間。給 cooldown_seconds 規則用，
    /// 防同 symbol 在 risk window 內被連續開單（signal 抖動 yolo）。
    /// 沒紀錄的 key 表示從沒交易過、或交易時間早於 cooldown 視窗。
    /// </summary>
    public Dictionary<string, DateTime> LastTradeBySymbol { get; set; } = new();
}

public class PositionEntry
{
    public string Symbol        { get; set; } = string.Empty;
    public string Exchange      { get; set; } = string.Empty;
    public decimal Quantity     { get; set; }
    public decimal MarketValue  { get; set; }
    public decimal UnrealizedPnl { get; set; }
}
