using ClosedXML.Excel;
using WorkLogAI.Core;

namespace WorkLogAI.Infrastructure;

public sealed class ClosedXmlWeeklyReportExporter : IWeeklyReportExporter
{
    private const string ReportName = "業務週報(USA太田)";

    public string CreateFileName(WeekRange range) =>
        $"{ReportName} {range.Start:yyyyMMdd}-{range.End:yyyyMMdd}.xlsx";

    public Task<string> ExportAsync(
        WeekRange range,
        IEnumerable<ReportRow> rows,
        string outputDirectory,
        ReportIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(identity);
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, CreateFileName(range));
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("業務週報");

        sheet.Range("A1:C1").Merge();
        sheet.Cell("A1").Value = $"{ReportName} {range.Start:yyyy/MM/dd}〜{range.End:yyyy/MM/dd}";
        sheet.Cell("D1").Value = $"{identity.CompanyName} / {identity.EmployeeName}";
        sheet.Range("A1:D1").Style.Font.Bold = true;
        sheet.Range("A1:D1").Style.Font.FontSize = 12;

        var headers = new[]
        {
            "日時",
            "業務項目",
            "活動内容",
            "結果・決定事項／今後の課題"
        };
        for (var column = 1; column <= headers.Length; column++)
        {
            sheet.Cell(3, column).Value = headers[column - 1];
        }

        sheet.Range("A3:D3").Style.Font.Bold = true;
        sheet.Range("A3:D3").Style.Fill.BackgroundColor = XLColor.LightGray;

        var rowNumber = 4;
        foreach (var row in rows.OrderBy(row => row.Date))
        {
            cancellationToken.ThrowIfCancellationRequested();
            sheet.Cell(rowNumber, 1).Value = row.Date.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(rowNumber, 1).Style.DateFormat.Format = "yyyy/MM/dd";
            sheet.Cell(rowNumber, 2).Value = row.WorkItem;
            sheet.Cell(rowNumber, 3).Value = row.Activity;
            sheet.Cell(rowNumber, 4).Value = row.ResultOrNext;
            rowNumber++;
        }

        var lastRow = Math.Max(3, rowNumber - 1);
        var reportRange = sheet.Range(3, 1, lastRow, 4);
        sheet.Range(1, 1, lastRow, 4).Style.Alignment.WrapText = true;
        reportRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        reportRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        reportRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        sheet.Column(1).Width = 13;
        sheet.Column(2).Width = 22;
        sheet.Column(3).Width = 48;
        sheet.Column(4).Width = 48;
        sheet.Rows(1, lastRow).AdjustToContents();

        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.PagesWide = 1;
        sheet.PageSetup.PagesTall = 0;
        sheet.PageSetup.PrintAreas.Add($"A1:D{lastRow}");
        sheet.SheetView.FreezeRows(3);

        workbook.SaveAs(path);
        return Task.FromResult(path);
    }
}
