using System.Globalization;
using Bricks4Agent.Reporting.Abstractions.Models;
using Bricks4Agent.Reporting.Abstractions.Services;
using ClosedXML.Excel;

namespace Bricks4Agent.Reporting.ClosedXmlAdapter;

public sealed class ClosedXmlExcelReportService : IExcelReportService
{
    private const string ExcelContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public Task<ReportFile> GenerateAsync(
        ExcelReportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Sheets.Count == 0)
        {
            throw new ArgumentException("Excel report must contain at least one sheet.", nameof(request));
        }

        using var workbook = new XLWorkbook();

        foreach (var sheet in request.Sheets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RenderSheet(workbook, sheet);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return Task.FromResult(new ReportFile
        {
            FileName = request.FileName,
            ContentType = ExcelContentType,
            Content = stream.ToArray()
        });
    }

    private static void RenderSheet(XLWorkbook workbook, ExcelSheetRequest sheet)
    {
        if (sheet.Columns.Count == 0)
        {
            throw new ArgumentException($"Sheet '{sheet.Name}' must define at least one column.", nameof(sheet));
        }

        var worksheet = workbook.Worksheets.Add(NormalizeSheetName(sheet.Name));

        for (var columnIndex = 0; columnIndex < sheet.Columns.Count; columnIndex++)
        {
            var column = sheet.Columns[columnIndex];
            var cell = worksheet.Cell(1, columnIndex + 1);
            cell.Value = column.Header;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
        }

        for (var rowIndex = 0; rowIndex < sheet.Rows.Count; rowIndex++)
        {
            var row = sheet.Rows[rowIndex];

            for (var columnIndex = 0; columnIndex < sheet.Columns.Count; columnIndex++)
            {
                var column = sheet.Columns[columnIndex];
                row.TryGetValue(column.Key, out var value);

                if (value is null && column.Required)
                {
                    throw new InvalidOperationException(
                        $"Row {rowIndex + 1} in sheet '{sheet.Name}' is missing required column '{column.Key}'.");
                }

                WriteCellValue(worksheet.Cell(rowIndex + 2, columnIndex + 1), column, value);
            }
        }

        if (sheet.FreezeHeaderRow)
        {
            worksheet.SheetView.FreezeRows(1);
        }

        worksheet.ColumnsUsed().AdjustToContents();
    }

    private static void WriteCellValue(IXLCell cell, ExcelColumnDefinition column, object? value)
    {
        if (value is null)
        {
            cell.Clear();
            return;
        }

        switch (column.ValueKind)
        {
            case ExcelColumnValueKind.Integer:
                cell.Value = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                break;

            case ExcelColumnValueKind.Decimal:
            case ExcelColumnValueKind.Currency:
                cell.Value = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                break;

            case ExcelColumnValueKind.Boolean:
                cell.Value = Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                break;

            case ExcelColumnValueKind.Date:
                cell.Value = ToDateTime(value, dateOnly: true);
                break;

            case ExcelColumnValueKind.DateTime:
                cell.Value = ToDateTime(value, dateOnly: false);
                break;

            default:
                cell.Value = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                break;
        }

        if (!string.IsNullOrWhiteSpace(column.Format))
        {
            cell.Style.NumberFormat.Format = column.Format;
        }
    }

    private static DateTime ToDateTime(object value, bool dateOnly)
    {
        DateTime dateTime = value switch
        {
            DateOnly date => date.ToDateTime(TimeOnly.MinValue),
            DateTime dt => dt,
            _ => Convert.ToDateTime(value, CultureInfo.InvariantCulture)
        };

        return dateOnly ? dateTime.Date : dateTime;
    }

    private static string NormalizeSheetName(string name)
    {
        var sheetName = string.IsNullOrWhiteSpace(name) ? "Sheet1" : name.Trim();
        var invalidChars = new[] { ':', '\\', '/', '?', '*', '[', ']' };

        foreach (var invalidChar in invalidChars)
        {
            sheetName = sheetName.Replace(invalidChar, '_');
        }

        return sheetName.Length <= 31 ? sheetName : sheetName[..31];
    }
}
