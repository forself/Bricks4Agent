namespace QuoteWorker.Models;

/// <summary>
/// OHLCV K 線資料（一根 bar）。
/// </summary>
public class OhlcvBar
{
    public string Symbol   { get; set; } = string.Empty;
    public string Type     { get; set; } = "stock"; // "stock" | "crypto"
    public string Interval { get; set; } = "1d";    // "1m","5m","15m","1h","4h","1d","1w"
    public DateTime OpenTime  { get; set; }
    public DateTime CloseTime { get; set; }
    public decimal Open    { get; set; }
    public decimal High    { get; set; }
    public decimal Low     { get; set; }
    public decimal Close   { get; set; }
    public decimal Volume  { get; set; }
}
