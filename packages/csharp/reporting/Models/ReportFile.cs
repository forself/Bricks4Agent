namespace Bricks4Agent.Reporting.Abstractions.Models;

public sealed record ReportFile
{
    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public required byte[] Content { get; init; }
}
