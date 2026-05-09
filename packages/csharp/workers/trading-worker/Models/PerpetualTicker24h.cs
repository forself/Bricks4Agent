namespace TradingWorker.Models;

/// <summary>
/// 永續合約 24h ticker 快照——從交易所拉、給 SymbolScreener 評分用。
///
/// 欄位是「交易所無關的 superset」：BingX 用得到的填、其他欄位 0；
/// 之後 OKX/Bybit 等加入時、各自 client 補 self-explanatory 的對應欄。
/// </summary>
public class PerpetualTicker24h
{
    public string Symbol         { get; set; } = string.Empty;
    public decimal LastPrice     { get; set; }
    public decimal HighPrice     { get; set; }
    public decimal LowPrice      { get; set; }
    public decimal OpenPrice     { get; set; }

    /// <summary>24h 成交量（base currency 計、例：BTC）</summary>
    public decimal Volume        { get; set; }

    /// <summary>24h 成交額（quote currency 計、例：USDT）。screener 主要看這個做流動性過濾。</summary>
    public decimal QuoteVolume   { get; set; }

    /// <summary>24h 漲跌絕對值</summary>
    public decimal PriceChange       { get; set; }

    /// <summary>24h 漲跌百分比</summary>
    public decimal PriceChangePercent { get; set; }

    public DateTime SnapshotAt   { get; set; } = DateTime.UtcNow;
}
