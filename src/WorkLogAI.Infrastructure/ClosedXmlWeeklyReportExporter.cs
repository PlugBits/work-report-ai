using ClosedXML.Excel;
using WorkLogAI.Core;

namespace WorkLogAI.Infrastructure;

public sealed class ClosedXmlWeeklyReportExporter : IWeeklyReportExporter
{
    private static readonly XLColor HeaderFillColor = XLColor.FromHtml("#2E74B5");
    private const string FontName = "Noto Sans JP";

    public string CreateFileName(WeekRange range, ReportIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var title = ReportFileNameSanitizer.Sanitize(identity.ReportTitle);
        return $"{title} {range.Start:yyyyMMdd}-{range.End:yyyyMMdd}.xlsx";
    }

    public Task<string> ExportAsync(
        WeekRange range,
        IEnumerable<ReportRow> rows,
        string outputDirectory,
        ReportIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var titleText =
            $"{identity.ReportTitle} {range.Start:yyyy/MM/dd}〜{range.End:yyyy/MM/dd}";
        return RenderAsync(
            titleText,
            CreateFileName(range, identity),
            rows,
            outputDirectory,
            identity,
            cancellationToken);
    }

    public string CreateMonthFileName(int year, int month, ReportIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var title = ReportFileNameSanitizer.Sanitize(identity.ReportTitle);
        return $"{title} 月次 {year:D4}{month:D2}.xlsx";
    }

    public Task<string> ExportMonthAsync(
        int year,
        int month,
        IEnumerable<ReportRow> rows,
        string outputDirectory,
        ReportIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var titleText = $"{identity.ReportTitle} {year}年{month}月 月次まとめ";
        return RenderAsync(
            titleText,
            CreateMonthFileName(year, month, identity),
            rows,
            outputDirectory,
            identity,
            cancellationToken);
    }

    private static Task<string> RenderAsync(
        string titleText,
        string fileName,
        IEnumerable<ReportRow> rows,
        string outputDirectory,
        ReportIdentity identity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(identity);
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, fileName);
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("業務週報");
        sheet.Style.Font.FontName = FontName;

        sheet.Range("A1:C1").Merge();
        sheet.Cell("A1").Value = titleText;
        sheet.Cell("D1").Value = identity.CompanyName;
        sheet.Cell("D2").Value = identity.EmployeeName;
        sheet.Range("A1:D2").Style.Font.Bold = true;
        sheet.Range("A1:D2").Style.Font.FontSize = 12;
        sheet.Range("A1:D2").Style.Font.FontName = FontName;
        sheet.Range("D1:D2").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

        var headers = new[]
        {
            "日時",
            "項目/案件・目標金額",
            "活動内容",
            "結果・決定事項/今後の課題"
        };
        for (var column = 1; column <= headers.Length; column++)
        {
            sheet.Cell(3, column).Value = headers[column - 1];
        }

        var headerRange = sheet.Range("A3:D3");
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Font.FontColor = XLColor.White;
        headerRange.Style.Font.FontName = FontName;
        headerRange.Style.Fill.BackgroundColor = HeaderFillColor;
        headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        headerRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        var dailyRows = DailyReportGrouper.Group(rows.OrderBy(row => row.Date));

        var employeeName = identity.EmployeeName?.Trim() ?? string.Empty;
        var surname = string.IsNullOrWhiteSpace(employeeName)
            ? null
            : employeeName.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

        var rowNumber = 4;
        foreach (var day in dailyRows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dateLines = new List<string> { CategoryLabel(day.Category), day.Date.ToString("yyyy/MM/dd") };
            if (!string.IsNullOrEmpty(surname))
            {
                dateLines.Add(surname);
            }

            sheet.Cell(rowNumber, 1).Value = string.Join("\n", dateLines);

            var workItemLines = day.Items
                .Select(item => $"{DailyReportGrouper.CircledNumber(item.Number)} {item.WorkItem}");
            sheet.Cell(rowNumber, 2).Value = string.Join("\n", workItemLines);

            var activityLines = day.Items
                .Select(item => $"{DailyReportGrouper.CircledNumber(item.Number)} {item.Activity}");
            sheet.Cell(rowNumber, 3).Value = string.Join("\n", activityLines);

            var resultLines = day.Items
                .Where(item => !string.IsNullOrWhiteSpace(item.ResultOrNext))
                .Select(item => $"{DailyReportGrouper.CircledNumber(item.Number)} {item.ResultOrNext}");
            sheet.Cell(rowNumber, 4).Value = string.Join("\n", resultLines);

            rowNumber++;
        }

        var lastRow = Math.Max(3, rowNumber - 1);

        var reportRange = sheet.Range(3, 1, lastRow, 4);
        sheet.Range(1, 1, lastRow, 4).Style.Alignment.WrapText = true;
        sheet.Range(1, 1, lastRow, 4).Style.Font.FontName = FontName;
        reportRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        reportRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        reportRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        sheet.Column(1).Width = 12;
        sheet.Column(2).Width = 26;
        sheet.Column(3).Width = 46;
        sheet.Column(4).Width = 42;
        sheet.Rows(1, lastRow).AdjustToContents();

        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.PagesWide = 1;
        sheet.PageSetup.PagesTall = 0;
        sheet.PageSetup.PrintAreas.Add($"A1:D{lastRow}");
        sheet.SheetView.FreezeRows(3);

        workbook.SaveAs(path);
        return Task.FromResult(path);
    }

    private static string CategoryLabel(string category) =>
        category == ReportCategories.External ? "社外" : "社内";
}

public static class ReportFileNameSanitizer
{
    public const string DefaultTitle = "業務週報";

    public static string Sanitize(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return DefaultTitle;
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(
            title.Select(character => invalidChars.Contains(character) ? '_' : character).ToArray());
        return sanitized;
    }
}
