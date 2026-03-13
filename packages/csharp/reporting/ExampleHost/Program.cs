using Bricks4Agent.Reporting.Abstractions.Examples;
using Bricks4Agent.Reporting.Abstractions.Services;
using Bricks4Agent.Reporting.ClosedXmlAdapter;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<IExcelReportService, ClosedXmlExcelReportService>();
builder.Services.AddScoped<IEmployeeReportQueryService, InMemoryEmployeeReportQueryService>();
builder.Services.AddScoped<ExportEmployeesReportUseCase>();

var app = builder.Build();

app.MapGet("/", () => Results.Json(new
{
    service = "reporting-example-host",
    endpoints = new[]
    {
        "/reports/employees/HR",
        "/reports/employees/IT",
        "/reports/employees/FIN"
    }
}));

app.MapGet("/reports/employees/{departmentCode}", async (
    string departmentCode,
    ExportEmployeesReportUseCase useCase,
    CancellationToken cancellationToken) =>
{
    var report = await useCase.ExecuteAsync(departmentCode, cancellationToken);
    return Results.File(report.Content, report.ContentType, report.FileName);
});

app.Run();

internal sealed class InMemoryEmployeeReportQueryService : IEmployeeReportQueryService
{
    private static readonly IReadOnlyList<EmployeeReportRow> Employees =
    [
        new(1001, "Alice Chen", "HR", "HR Manager", new DateOnly(2021, 3, 15), 78000m),
        new(1002, "Brian Lin", "HR", "Recruiter", new DateOnly(2023, 7, 1), 54000m),
        new(2001, "Cindy Wang", "IT", "Platform Engineer", new DateOnly(2020, 11, 2), 92000m),
        new(2002, "Daniel Wu", "IT", "Security Analyst", new DateOnly(2022, 6, 20), 81000m),
        new(3001, "Eva Tsai", "FIN", "Finance Director", new DateOnly(2019, 4, 8), 110000m)
    ];

    public Task<IReadOnlyList<EmployeeReportRow>> ListByDepartmentAsync(
        string departmentCode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var rows = Employees
            .Where(row => string.Equals(
                row.Department,
                departmentCode,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return Task.FromResult<IReadOnlyList<EmployeeReportRow>>(rows);
    }
}
