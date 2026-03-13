using Bricks4Agent.Reporting.Abstractions.Models;
using Bricks4Agent.Reporting.Abstractions.Services;

namespace Bricks4Agent.Reporting.Abstractions.Examples;

public interface IEmployeeReportQueryService
{
    Task<IReadOnlyList<EmployeeReportRow>> ListByDepartmentAsync(
        string departmentCode,
        CancellationToken cancellationToken = default);
}

public sealed record EmployeeReportRow(
    int EmployeeId,
    string Name,
    string Department,
    string Title,
    DateOnly HireDate,
    decimal Salary);

public sealed class ExportEmployeesReportUseCase
{
    private readonly IEmployeeReportQueryService _queryService;
    private readonly IExcelReportService _excelReportService;

    public ExportEmployeesReportUseCase(
        IEmployeeReportQueryService queryService,
        IExcelReportService excelReportService)
    {
        _queryService = queryService;
        _excelReportService = excelReportService;
    }

    public async Task<ReportFile> ExecuteAsync(
        string departmentCode,
        CancellationToken cancellationToken = default)
    {
        var rows = await _queryService.ListByDepartmentAsync(departmentCode, cancellationToken);

        var request = new ExcelReportRequest
        {
            FileName = $"employees-{departmentCode}.xlsx",
            AuditLabel = "employees-report-export",
            GeneratedBy = nameof(ExportEmployeesReportUseCase),
            Sheets =
            [
                new ExcelSheetRequest
                {
                    Name = "Employees",
                    Columns =
                    [
                        new ExcelColumnDefinition("employeeId", "Employee ID", ExcelColumnValueKind.Integer, Required: true),
                        new ExcelColumnDefinition("name", "Name", ExcelColumnValueKind.Text, Required: true),
                        new ExcelColumnDefinition("department", "Department", ExcelColumnValueKind.Text, Required: true),
                        new ExcelColumnDefinition("title", "Title", ExcelColumnValueKind.Text),
                        new ExcelColumnDefinition("hireDate", "Hire Date", ExcelColumnValueKind.Date, Format: "yyyy-MM-dd"),
                        new ExcelColumnDefinition("salary", "Salary", ExcelColumnValueKind.Currency, Format: "#,##0.00")
                    ],
                    Rows = rows.Select(row => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
                    {
                        ["employeeId"] = row.EmployeeId,
                        ["name"] = row.Name,
                        ["department"] = row.Department,
                        ["title"] = row.Title,
                        ["hireDate"] = row.HireDate,
                        ["salary"] = row.Salary
                    }).ToArray()
                }
            ]
        };

        return await _excelReportService.GenerateAsync(request, cancellationToken);
    }
}
