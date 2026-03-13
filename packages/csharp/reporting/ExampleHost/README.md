# Reporting Example Host

This is a minimal ASP.NET Core host showing the intended wiring for the
reporting abstraction and adapter boundary.

It demonstrates:

- generated-style use case code depending on `Bricks4Agent.Reporting.Abstractions`
- host-level registration of `ClosedXmlExcelReportService`
- an HTTP endpoint returning an Excel file without exposing `ClosedXML` to the generated layer

## Run

```bash
dotnet run --project packages/csharp/reporting/ExampleHost/ReportingExampleHost.csproj --urls http://127.0.0.1:5087
```

Then request:

```bash
curl.exe -OJ http://127.0.0.1:5087/reports/employees/HR
```

Available sample departments:

- `HR`
- `IT`
- `FIN`
