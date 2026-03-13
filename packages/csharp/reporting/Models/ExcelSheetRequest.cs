namespace Bricks4Agent.Reporting.Abstractions.Models;

public sealed record ExcelSheetRequest
{
    public required string Name { get; init; }

    public IReadOnlyList<ExcelColumnDefinition> Columns { get; init; } =
        Array.Empty<ExcelColumnDefinition>();

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows { get; init; } =
        Array.Empty<IReadOnlyDictionary<string, object?>>();

    public bool FreezeHeaderRow { get; init; } = true;
}
