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

For a concrete host example, see:

- [Program.cs](/d:/Bricks4Agent/packages/csharp/reporting/ExampleHost/Program.cs)
- [ReportingExampleHost.csproj](/d:/Bricks4Agent/packages/csharp/reporting/ExampleHost/ReportingExampleHost.csproj)

## Dependency boundary

Generated code should reference only:

- `Bricks4Agent.Reporting.Abstractions`

Only this adapter package should reference:

- `ClosedXML`
