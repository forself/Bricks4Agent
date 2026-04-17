namespace QuoteWorker.Models;

public class QuoteFetchJob
{
    public string   JobId        { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public DateTime CreatedAt    { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt   { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>pending | running | success | failed</summary>
    public string Status         { get; set; } = "pending";

    public int TotalSymbols      { get; set; }
    public int FetchedCount      { get; set; }
    public int ErrorCount        { get; set; }

    public List<QuoteResult> Results { get; set; } = new();
    public List<string>      Errors  { get; set; } = new();
    public string?           FatalError { get; set; }

    public double DurationSeconds =>
        StartedAt.HasValue && CompletedAt.HasValue
            ? (CompletedAt.Value - StartedAt.Value).TotalSeconds
            : 0;
}
