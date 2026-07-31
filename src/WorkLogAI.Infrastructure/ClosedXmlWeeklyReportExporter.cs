using ClosedXML.Excel;
using WorkLogAI.Core;

namespace WorkLogAI.Infrastructure;

public sealed class ClosedXmlWeeklyReportExporter : IWeeklyReportExporter
{
    private static readonly XLColor HeaderFillColor = XLColor.FromHtml("#2E74B5");
    private const string FontName = "BIZ UDPゴシック";

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

        const int MinimumBlockRows = 4;

        var rowNumber = 4;
        var blockBoundaries = new List<(int UpperRow, int LowerRow)>();

        foreach (var day in dailyRows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var contentRowNumber = rowNumber;

            sheet.Cell(contentRowNumber, 1).Value =
                $"{day.Date:yyyy/MM/dd}\n({JapaneseWeekday(day.Date.DayOfWeek)})";

            var workItemLines = day.Items
                .Select(item => $"{DailyReportGrouper.CircledNumber(item.Number)} {item.WorkItem}");
            sheet.Cell(contentRowNumber, 2).Value = string.Join("\n", workItemLines);

            var activityLines = day.Items
                .Select(item => $"{DailyReportGrouper.CircledNumber(item.Number)} {item.Activity}");
            sheet.Cell(contentRowNumber, 3).Value = string.Join("\n", activityLines);

            var resultLines = day.Items
                .Where(item => !string.IsNullOrWhiteSpace(item.ResultOrNext))
                .Select(item => $"{DailyReportGrouper.CircledNumber(item.Number)} {item.ResultOrNext}");
            sheet.Cell(contentRowNumber, 4).Value = string.Join("\n", resultLines);

            rowNumber++;

            // Every day's block — its content row plus blank rows — spans a
            // uniform minimum of MinimumBlockRows sheet rows, so each day
            // occupies the same visual height regardless of item count. The
            // blank rows are ordinary empty rows within the continuous
            // bordered grid, with no forced height, so they behave like any
            // other row under Excel's auto-fit.
            const int ContentRowCount = 1;
            var blankRowCount = Math.Max(1, MinimumBlockRows - ContentRowCount);
            for (var index = 0; index < blankRowCount; index++)
            {
                blockBoundaries.Add((rowNumber - 1, rowNumber));
                rowNumber++;
            }
        }

        var lastRow = Math.Max(3, rowNumber - 1);

        var reportRange = sheet.Range(3, 1, lastRow, 4);
        sheet.Range(1, 1, lastRow, 4).Style.Alignment.WrapText = true;
        sheet.Range(1, 1, lastRow, 4).Style.Font.FontName = FontName;
        reportRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        reportRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        reportRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        foreach (var (upperRow, lowerRow) in blockBoundaries)
        {
            // Each day block (content row + its blank rows) is one visual
            // cell region per column: drop the horizontal line at every
            // internal boundary within the block. Both adjacent edges must
            // be cleared — Excel renders a line if either side still has
            // one. The block's own bottom edge (the last blank row's bottom
            // border) is never touched here, so it stays Thin as the
            // boundary before the next day's block; left/right borders are
            // left untouched too.
            sheet.Range(upperRow, 1, upperRow, 4).Style.Border.BottomBorder = XLBorderStyleValues.None;
            sheet.Range(lowerRow, 1, lowerRow, 4).Style.Border.TopBorder = XLBorderStyleValues.None;
        }

        sheet.Column(1).Width = 20.14; // 146 px
        sheet.Column(2).Width = 39.71; // 283 px
        sheet.Column(3).Width = 69.29; // 490 px
        sheet.Column(4).Width = 60.14; // 426 px
        sheet.Rows(1, lastRow).AdjustToContents();

        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.PagesWide = 1;
        sheet.PageSetup.PagesTall = 0;
        sheet.PageSetup.PrintAreas.Add($"A1:D{lastRow}");
        sheet.SheetView.FreezeRows(3);

        workbook.SaveAs(path);
        return Task.FromResult(path);
    }

    private static string JapaneseWeekday(DayOfWeek dayOfWeek) => dayOfWeek switch
    {
        DayOfWeek.Monday => "月",
        DayOfWeek.Tuesday => "火",
        DayOfWeek.Wednesday => "水",
        DayOfWeek.Thursday => "木",
        DayOfWeek.Friday => "金",
        DayOfWeek.Saturday => "土",
        DayOfWeek.Sunday => "日",
        _ => "?"
    };
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
