using Bricks4Agent.Reporting.Abstractions.Models;

namespace Bricks4Agent.Reporting.Abstractions.Services;

public interface IExcelReportService
{
    Task<ReportFile> GenerateAsync(
        ExcelReportRequest request,
        CancellationToken cancellationToken = default);
}
