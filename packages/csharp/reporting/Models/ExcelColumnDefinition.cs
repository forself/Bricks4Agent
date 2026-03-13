namespace Bricks4Agent.Reporting.Abstractions.Models;

public sealed record ExcelColumnDefinition(
    string Key,
    string Header,
    ExcelColumnValueKind ValueKind = ExcelColumnValueKind.Text,
    bool Required = false,
    string? Format = null);
