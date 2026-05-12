namespace TradingWorker.Models;

/// <summary>
/// 永續合約規格快照——從交易所 contract info endpoint 取、用於 broker 端 pre-flight 檢查。
/// 取代硬編在 broker-core/SymbolSpecs.cs 裡的維護成本表、自動跟著交易所上下架。
/// </summary>
public class PerpetualContract
{
    public string Symbol           { get; set; } = "";
    public decimal MinQty          { get; set; }   // 最小單量
    public decimal QtyStep         { get; set; }   // 數量精度
    public decimal MinNotional     { get; set; }   // 最小名目 USDT
    public int     MaxLeverage     { get; set; }
    public string  QuoteCurrency   { get; set; } = "USDT";
    public bool    Trading         { get; set; } = true;   // false = 暫停 / 已下架
    public DateTime SnapshotAt     { get; set; }
}
