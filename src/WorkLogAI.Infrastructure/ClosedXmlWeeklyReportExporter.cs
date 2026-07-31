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
        sheet.Cell("E1").Value = identity.CompanyName;
        sheet.Cell("E2").Value = identity.EmployeeName;
        sheet.Range("A1:E2").Style.Font.Bold = true;
        sheet.Range("A1:E2").Style.Font.FontSize = 12;
        sheet.Range("A1:E2").Style.Font.FontName = FontName;
        sheet.Range("E1:E2").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

        // Row 3 is left completely blank — a one-row spacer between the
        // title area and the header — with no content, borders, or fill of
        // its own; the header moves down to row 4 and data starts at row 5.
        var headers = new[]
        {
            "日付",
            "曜日",
            "項目/案件・目標金額",
            "活動内容",
            "結果・決定事項/今後の課題"
        };
        for (var column = 1; column <= headers.Length; column++)
        {
            sheet.Cell(4, column).Value = headers[column - 1];
        }

        var headerRange = sheet.Range("A4:E4");
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Font.FontColor = XLColor.White;
        headerRange.Style.Font.FontName = FontName;
        headerRange.Style.Fill.BackgroundColor = HeaderFillColor;
        headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        headerRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        var dailyRows = DailyReportGrouper.Group(rows.OrderBy(row => row.Date));

        const int MinimumBlockRows = 4;

        var rowNumber = 5;
        var blockLastRows = new List<int>();

        foreach (var day in dailyRows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // One sheet row per item, so the 項目/活動内容/結果 cells for a given
            // item always sit on the same row and stay aligned once text
            // wraps — no more packing a day's items into one multi-line cell.
            for (var itemIndex = 0; itemIndex < day.Items.Count; itemIndex++)
            {
                var item = day.Items[itemIndex];
                var label = DailyReportGrouper.CircledNumber(item.Number);

                if (itemIndex == 0)
                {
                    // The date and weekday cells appear once, on the block's
                    // first row only; every other row in the block leaves
                    // columns A and B empty.
                    sheet.Cell(rowNumber, 1).Value = $"{day.Date:yyyy/MM/dd}";
                    sheet.Cell(rowNumber, 2).Value = $"({JapaneseWeekday(day.Date.DayOfWeek)})";
                }

                sheet.Cell(rowNumber, 3).Value = $"{label} {item.WorkItem}";
                sheet.Cell(rowNumber, 4).Value = $"{label} {item.Activity}";
                if (!string.IsNullOrWhiteSpace(item.ResultOrNext))
                {
                    sheet.Cell(rowNumber, 5).Value = $"{label} {item.ResultOrNext}";
                }

                rowNumber++;
            }

            // Every day's block — its item rows plus trailing blank rows —
            // spans a uniform minimum of MinimumBlockRows sheet rows, so
            // each day occupies at least the same visual height regardless
            // of item count, and always keeps at least one trailing blank
            // row even when the item count already meets or exceeds the
            // minimum. The blank rows are ordinary empty rows with no
            // forced height, so they behave like any other row under
            // Excel's auto-fit.
            var blankRowCount = Math.Max(1, MinimumBlockRows - day.Items.Count);
            rowNumber += blankRowCount;

            blockLastRows.Add(rowNumber - 1);
        }

        var lastRow = Math.Max(4, rowNumber - 1);

        var reportRange = sheet.Range(4, 1, lastRow, 5);
        sheet.Range(1, 1, lastRow, 5).Style.Alignment.WrapText = true;
        sheet.Range(1, 1, lastRow, 5).Style.Font.FontName = FontName;
        reportRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

        // Constructive borders: draw only the lines that are actually
        // wanted, never a blanket InsideBorder/OutsideBorder over the data
        // area followed by clearing specific edges back off — that pattern
        // renders inconsistently across viewers. Row 4 (the header) already
        // got its own full thin border above; everything from row 5 down is
        // built here from nothing.
        if (lastRow >= 5)
        {
            // Vertical lines: a left edge on every column A-E (i.e. the
            // four internal dividers plus the table's outer left edge) and
            // the table's outer right edge on column E, continuous for
            // every row of every block — items and blanks alike.
            sheet.Range(5, 1, lastRow, 5).Style.Border.LeftBorder = XLBorderStyleValues.Thin;
            sheet.Range(5, 5, lastRow, 5).Style.Border.RightBorder = XLBorderStyleValues.Thin;

            // Horizontal lines: only the last row of each day block gets a
            // bottom edge — the boundary before the next day (and, for the
            // final block, the bottom of the table). No other horizontal
            // edge is drawn anywhere in the data area: not between item
            // rows, not around blank rows.
            foreach (var blockLastRow in blockLastRows)
            {
                sheet.Range(blockLastRow, 1, blockLastRow, 5).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            }
        }

        sheet.Column(1).Width = 20.14; // 146 px
        sheet.Column(2).Width = 7.0;   // ~54 px
        sheet.Column(3).Width = 39.71; // 283 px
        sheet.Column(4).Width = 69.29; // 490 px
        sheet.Column(5).Width = 60.14; // 426 px
        sheet.Rows(1, lastRow).AdjustToContents();

        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
        sheet.PageSetup.PagesWide = 1;
        sheet.PageSetup.PagesTall = 0;
        sheet.PageSetup.PrintAreas.Add($"A1:E{lastRow}");
        sheet.SheetView.FreezeRows(4);

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
