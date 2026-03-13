# Reporting Abstractions

This package defines generator-friendly reporting contracts for backend code.

The intended layering is:

1. Generated application code
2. Platform abstraction package
3. Adapter implementation package
4. Host or composition root

Generated code must depend only on:

- .NET standard library
- `Bricks4Agent.Reporting.Abstractions`
- approved application interfaces such as query services

Generated code must not depend directly on:

- `ClosedXML`
- `EPPlus`
- `NPOI`
- raw filesystem writes for report generation

## Why this exists

Low-level AI and half-rule generators are good at:

- loading business data
- mapping rows into a request contract
- calling a stable interface
- returning a report file

They are not reliable at:

- picking and using third-party Excel libraries directly
- styling workbooks consistently
- handling OpenXML details
- making adapter-level architectural decisions

This package narrows the problem so generated code only composes
`ExcelReportRequest` and calls `IExcelReportService`.

## Files

- `Models/ExcelReportRequest.cs`
- `Models/ExcelSheetRequest.cs`
- `Models/ExcelColumnDefinition.cs`
- `Models/ReportFile.cs`
- `Services/IExcelReportService.cs`
- `Examples/ExportEmployeesReportUseCase.cs`
- `ClosedXmlAdapter/ClosedXmlExcelReportService.cs`
- `ExampleHost/Program.cs`
- `CapabilitySchemas/excel-report.enterprise.json`

## Example flow

1. A controller receives an export request.
2. A use case loads business rows from a query service.
3. The use case builds `ExcelReportRequest`.
4. The use case calls `IExcelReportService.GenerateAsync(...)`.
5. An adapter package renders the workbook using an approved third-party library.
6. The host returns or stores the resulting `ReportFile`.

## Local verification

Validate dependency boundaries:

```bash
npm run validate:dotnet-deps
```

Validate source-level API usage boundaries:

```bash
npm run validate:dotnet-api-usage
```

Run both governance checks together:

```bash
npm run validate:backend-governance
```

Run the example host:

```bash
dotnet run --project packages/csharp/reporting/ExampleHost/ReportingExampleHost.csproj --urls http://127.0.0.1:5087
```
