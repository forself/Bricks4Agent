namespace Bricks4Agent.Reporting.Abstractions.Models;

public sealed record ExcelReportRequest
{
    public required string FileName { get; init; }

    public string AuditLabel { get; init; } = string.Empty;

    public string GeneratedBy { get; init; } = string.Empty;

    public IReadOnlyList<ExcelSheetRequest> Sheets { get; init; } =
        Array.Empty<ExcelSheetRequest>();
}
