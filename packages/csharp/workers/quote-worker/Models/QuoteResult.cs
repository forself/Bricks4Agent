namespace QuoteWorker.Models;

public class QuoteResult
{
    public string Symbol       { get; set; } = string.Empty;
    public string Name         { get; set; } = string.Empty;
    public decimal Price       { get; set; }
    public decimal Change      { get; set; }
    public decimal ChangePercent { get; set; }
    public string Currency     { get; set; } = "USD";
    public decimal MarketCap   { get; set; }
    public decimal Volume24h   { get; set; }
    public string Type         { get; set; } = "stock"; // "stock" | "crypto"
    public DateTime FetchedAt  { get; set; } = DateTime.UtcNow;
}
