namespace Broker.Services;

/// <summary>A single web source gathered for a report: search metadata plus optionally fetched body text.</summary>
public sealed record WebReportSource(string Title, string Url, string Snippet, string Content = "");

/// <summary>The synthesized report plus the sources it cites.</summary>
public sealed class WebReportResult
{
    public string Topic { get; init; } = string.Empty;
    public string Markdown { get; init; } = string.Empty;
    public IReadOnlyList<WebReportSource> Sources { get; init; } = [];

    /// <summary>True when the LLM produced the report body; false when the deterministic source-summary fallback was used.</summary>
    public bool LlmSucceeded { get; init; }

    public string FileName { get; init; } = string.Empty;
}

/// <summary>Seam over web search + fetch so the synthesis pipeline can be unit-tested without network access.</summary>
public interface IWebContentProvider
{
    Task<IReadOnlyList<WebReportSource>> SearchAsync(string topic, int maxSources, CancellationToken ct);

    Task<string> FetchAsync(string url, int maxLength, CancellationToken ct);
}

/// <summary>Seam over the LLM so synthesis can be faked in tests; returns null when no model output is available.</summary>
public interface IWebReportLlm
{
    Task<string?> SynthesizeAsync(string prompt, CancellationToken ct);
}
