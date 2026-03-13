# ClosedXML Adapter

This package is the adapter-layer implementation for
`Bricks4Agent.Reporting.Abstractions`.

Allowed responsibility:

- map `ExcelReportRequest` into a workbook
- apply worksheet structure and formatting
- serialize the workbook into `ReportFile`

Forbidden responsibility:

- querying business data
- deciding report use-case logic
- defining generated-layer controller or use-case policy

## Registration

```csharp
builder.Services.AddScoped<IExcelReportService, ClosedXmlExcelReportService>();
```

## Dependency boundary

Generated code should reference only:

- `Bricks4Agent.Reporting.Abstractions`

Only this adapter package should reference:

- `ClosedXML`
