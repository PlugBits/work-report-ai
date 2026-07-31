using ClosedXML.Excel;
using WorkLogAI.Core;
using WorkLogAI.Infrastructure;

namespace WorkLogAI.Tests;

public sealed class ExcelExportTests
{
    [Fact]
    public async Task Export_has_exact_japanese_layout_and_chronological_day_rows()
    {
        using var temporary = new TemporaryDirectory();
        var exporter = new ClosedXmlWeeklyReportExporter();
        var range = new WeekRange(new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 2));
        var rows = new[]
        {
            new ReportRow(new DateOnly(2026, 7, 30), "手動メモ", "二番目", ""),
            new ReportRow(new DateOnly(2026, 7, 28), "手動メモ", "最初", "")
        };

        var path = await exporter.ExportAsync(
            range,
            rows,
            temporary.Path,
            new ReportIdentity("サンプル株式会社", "山田 太郎"));

        Assert.Equal(
            "業務週報 20260727-20260802.xlsx",
            Path.GetFileName(path));
        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet("業務週報");
        Assert.Equal("業務週報 2026/07/27〜2026/08/02", sheet.Cell("A1").GetString());
        Assert.Equal("サンプル株式会社", sheet.Cell("E1").GetString());
        Assert.Equal("山田 太郎", sheet.Cell("E2").GetString());
        Assert.Equal(
            new[] { "日付", "曜日", "項目/案件・目標金額", "活動内容", "結果・決定事項/今後の課題" },
            sheet.Range("A4:E4").Cells().Select(cell => cell.GetString()).ToArray());

        // Row 3 is a completely blank spacer row between the title area and
        // the header: no content, no borders, no fill.
        Assert.All(
            sheet.Range("A3:E3").Cells(),
            cell =>
            {
                Assert.True(cell.IsEmpty());
                Assert.Equal(XLBorderStyleValues.None, cell.Style.Border.TopBorder);
                Assert.Equal(XLBorderStyleValues.None, cell.Style.Border.BottomBorder);
                Assert.Equal(XLBorderStyleValues.None, cell.Style.Border.LeftBorder);
                Assert.Equal(XLBorderStyleValues.None, cell.Style.Border.RightBorder);
                Assert.NotEqual(XLColor.FromHtml("#2E74B5"), cell.Style.Fill.BackgroundColor);
            });

        // Date and weekday are separate cells now: A holds the date only, B
        // holds the single-character Japanese weekday in parentheses.
        // 2026/07/28 is a Tuesday.
        Assert.Equal("2026/07/28", sheet.Cell("A5").GetString());
        Assert.Equal("(火)", sheet.Cell("B5").GetString());
        Assert.Equal("① 最初", sheet.Cell("D5").GetString());

        // Each day block is a uniform 4 rows (1 item row + 3 blanks), so the
        // second date's item row starts exactly 4 rows after the first.
        // 2026/07/30 is a Thursday.
        Assert.Equal("2026/07/30", sheet.Cell("A9").GetString());
        Assert.Equal("(木)", sheet.Cell("B9").GetString());
        Assert.Equal("① 二番目", sheet.Cell("D9").GetString());

        Assert.Equal(string.Empty, sheet.Cell("A6").GetString());
        Assert.Equal(string.Empty, sheet.Cell("A7").GetString());
        Assert.Equal(string.Empty, sheet.Cell("A8").GetString());
        Assert.Equal(string.Empty, sheet.Cell("A10").GetString());
        Assert.Equal(string.Empty, sheet.Cell("A11").GetString());
        Assert.Equal(string.Empty, sheet.Cell("A12").GetString());
        Assert.Equal(string.Empty, sheet.Cell("B6").GetString());
        Assert.Equal(string.Empty, sheet.Cell("B10").GetString());

        Assert.All(
            sheet.Range("A4:E12").Cells(),
            cell => Assert.True(cell.Style.Alignment.WrapText));
        Assert.Equal(XLPageOrientation.Landscape, sheet.PageSetup.PageOrientation);
        Assert.Equal(XLPaperSize.A4Paper, sheet.PageSetup.PaperSize);
        Assert.Equal(1, sheet.PageSetup.PagesWide);
    }

    [Fact]
    public async Task Export_uses_blue_fill_and_white_bold_text_on_header_row()
    {
        using var temporary = new TemporaryDirectory();
        var exporter = new ClosedXmlWeeklyReportExporter();
        var range = new WeekRange(new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 2));
        var rows = new[]
        {
            new ReportRow(new DateOnly(2026, 7, 28), "手動メモ", "最初", "")
        };

        var path = await exporter.ExportAsync(
            range,
            rows,
            temporary.Path,
            new ReportIdentity("サンプル株式会社", "山田 太郎"));

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet("業務週報");

        Assert.All(
            sheet.Range("A4:E4").Cells(),
            cell =>
            {
                Assert.True(cell.Style.Font.Bold);
                Assert.Equal(XLColor.White, cell.Style.Font.FontColor);
                Assert.Equal(XLColor.FromHtml("#2E74B5"), cell.Style.Fill.BackgroundColor);
            });
    }

    [Fact]
    public async Task Export_writes_one_row_per_item_with_aligned_numbering_and_min_padded_blanks()
    {
        using var temporary = new TemporaryDirectory();
        var exporter = new ClosedXmlWeeklyReportExporter();
        var range = new WeekRange(new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 2));
        var rows = new[]
        {
            new ReportRow(new DateOnly(2026, 7, 28), "案件A", "最初の活動", ""),
            new ReportRow(new DateOnly(2026, 7, 28), "案件B", "二番目の活動", "完了"),
            new ReportRow(new DateOnly(2026, 7, 30), "案件C", "三番目の活動", "")
        };

        var path = await exporter.ExportAsync(
            range,
            rows,
            temporary.Path,
            new ReportIdentity("サンプル株式会社", "山田 太郎"));

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet("業務週報");

        // 7/28 has two items -> one sheet row per item (rows 5-6), so the
        // 項目/活動内容/結果 cells for each item sit on the same row. The date
        // and weekday cells appear only on the block's first row (5); row
        // 6's A/B cells are empty. Padding brings the block to the uniform
        // minimum of 4 rows, so blanks fill rows 7-8 and the next date's
        // block starts at row 9.
        Assert.Equal("2026/07/28", sheet.Cell("A5").GetString());
        Assert.Equal("(火)", sheet.Cell("B5").GetString());
        Assert.Equal("① 案件A", sheet.Cell("C5").GetString());
        Assert.Equal("① 最初の活動", sheet.Cell("D5").GetString());
        Assert.Equal(string.Empty, sheet.Cell("E5").GetString());

        Assert.Equal(string.Empty, sheet.Cell("A6").GetString());
        Assert.Equal(string.Empty, sheet.Cell("B6").GetString());
        Assert.Equal("② 案件B", sheet.Cell("C6").GetString());
        Assert.Equal("② 二番目の活動", sheet.Cell("D6").GetString());
        Assert.Equal("② 完了", sheet.Cell("E6").GetString());

        Assert.Equal(string.Empty, sheet.Cell("A7").GetString());
        Assert.Equal(string.Empty, sheet.Cell("C7").GetString());
        Assert.Equal(string.Empty, sheet.Cell("A8").GetString());
        Assert.Equal(string.Empty, sheet.Cell("C8").GetString());

        Assert.Equal("2026/07/30", sheet.Cell("A9").GetString());
        Assert.Equal("(木)", sheet.Cell("B9").GetString());
        Assert.Equal("① 案件C", sheet.Cell("C9").GetString());
        Assert.Equal("① 三番目の活動", sheet.Cell("D9").GetString());
        Assert.Equal(string.Empty, sheet.Cell("E9").GetString());

        Assert.Equal(string.Empty, sheet.Cell("A10").GetString());
        Assert.Equal(string.Empty, sheet.Cell("A11").GetString());
        Assert.Equal(string.Empty, sheet.Cell("A12").GetString());
    }

    [Fact]
    public async Task Export_pads_a_full_four_item_day_with_exactly_one_trailing_blank_row()
    {
        using var temporary = new TemporaryDirectory();
        var exporter = new ClosedXmlWeeklyReportExporter();
        var range = new WeekRange(new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 2));
        var rows = new[]
        {
            new ReportRow(new DateOnly(2026, 7, 28), "案件1", "活動1", ""),
            new ReportRow(new DateOnly(2026, 7, 28), "案件2", "活動2", ""),
            new ReportRow(new DateOnly(2026, 7, 28), "案件3", "活動3", ""),
            new ReportRow(new DateOnly(2026, 7, 28), "案件4", "活動4", ""),
            new ReportRow(new DateOnly(2026, 7, 30), "案件5", "活動5", "")
        };

        var path = await exporter.ExportAsync(
            range,
            rows,
            temporary.Path,
            new ReportIdentity("サンプル株式会社", "山田 太郎"));

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet("業務週報");

        // 4 items already meet the minimum-4-rows floor, but the block must
        // still carry at least one trailing blank row (row 9), so the next
        // date's block starts at row 10, not row 9.
        Assert.Equal("① 案件1", sheet.Cell("C5").GetString());
        Assert.Equal("② 案件2", sheet.Cell("C6").GetString());
        Assert.Equal("③ 案件3", sheet.Cell("C7").GetString());
        Assert.Equal("④ 案件4", sheet.Cell("C8").GetString());
        Assert.Equal(string.Empty, sheet.Cell("C9").GetString());
        Assert.Equal(string.Empty, sheet.Cell("A9").GetString());
        Assert.Equal(string.Empty, sheet.Cell("B9").GetString());

        Assert.Equal("2026/07/30", sheet.Cell("A10").GetString());
        Assert.Equal("(木)", sheet.Cell("B10").GetString());
        Assert.Equal("① 案件5", sheet.Cell("C10").GetString());
    }

    [Fact]
    public async Task Export_creates_no_merges_in_the_data_area()
    {
        using var temporary = new TemporaryDirectory();
        var exporter = new ClosedXmlWeeklyReportExporter();
        var range = new WeekRange(new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 2));
        var rows = new[]
        {
            new ReportRow(new DateOnly(2026, 7, 28), "手動メモ", "最初", ""),
            new ReportRow(new DateOnly(2026, 7, 28), "手動メモ", "二番目", ""),
            new ReportRow(new DateOnly(2026, 7, 30), "手動メモ", "三番目", "")
        };

        var path = await exporter.ExportAsync(
            range,
            rows,
            temporary.Path,
            new ReportIdentity("サンプル株式会社", "山田 太郎"));

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet("業務週報");

        var dataAreaMerges = sheet.MergedRanges
            .Where(mergedRange => mergedRange.RangeAddress.FirstAddress.RowNumber >= 3)
            .ToArray();
        Assert.Empty(dataAreaMerges);

        // The title row merge (A1:C1) is unaffected.
        Assert.Contains(
            sheet.MergedRanges,
            mergedRange => mergedRange.RangeAddress.ToString() == "A1:C1");
    }

    [Fact]
    public async Task Export_draws_only_constructive_borders_no_internal_horizontal_lines()
    {
        using var temporary = new TemporaryDirectory();
        var exporter = new ClosedXmlWeeklyReportExporter();
        var range = new WeekRange(new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 2));
        var rows = new[]
        {
            new ReportRow(new DateOnly(2026, 7, 28), "案件A", "活動A", ""),
            new ReportRow(new DateOnly(2026, 7, 30), "案件B", "活動B", "")
        };

        var path = await exporter.ExportAsync(
            range,
            rows,
            temporary.Path,
            new ReportIdentity("サンプル株式会社", "山田 太郎"));

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet("業務週報");

        // First day's block: item row 5, blank rows 6-8. Every row in the
        // data area — item and blank alike — has a Thin LeftBorder on every
        // column A-E and a Thin RightBorder on column E only (constructive
        // vertical lines), and no TopBorder/BottomBorder anywhere except the
        // block's last row, which carries a Thin BottomBorder as the
        // boundary before the next day's block.
        for (var row = 5; row <= 8; row++)
        {
            foreach (var column in new[] { 1, 2, 3, 4, 5 })
            {
                Assert.Equal(
                    XLBorderStyleValues.Thin,
                    sheet.Cell(row, column).Style.Border.LeftBorder);
            }

            Assert.Equal(XLBorderStyleValues.Thin, sheet.Cell(row, 5).Style.Border.RightBorder);
            Assert.Equal(XLBorderStyleValues.None, sheet.Cell(row, 1).Style.Border.RightBorder);

            var expectedBottom = row == 8 ? XLBorderStyleValues.Thin : XLBorderStyleValues.None;
            foreach (var column in new[] { 1, 2, 3, 4, 5 })
            {
                Assert.Equal(expectedBottom, sheet.Cell(row, column).Style.Border.BottomBorder);
            }

            foreach (var column in new[] { 1, 2, 3, 4, 5 })
            {
                Assert.Equal(
                    XLBorderStyleValues.None,
                    sheet.Cell(row, column).Style.Border.TopBorder);
            }
        }

        // Blank rows remain completely empty.
        Assert.Equal(string.Empty, sheet.Cell("A6").GetString());
        Assert.Equal(string.Empty, sheet.Cell("B6").GetString());
        Assert.Equal(string.Empty, sheet.Cell("C6").GetString());
        Assert.Equal(string.Empty, sheet.Cell("D6").GetString());
        Assert.Equal(string.Empty, sheet.Cell("E6").GetString());

        // Second (last) day gets the same constructive treatment, including
        // its own trailing blanks — every date's block is uniform, and its
        // own last row (12) carries the table's bottom boundary.
        Assert.Equal("2026/07/30", sheet.Cell("A9").GetString());
        Assert.Equal("(木)", sheet.Cell("B9").GetString());
        for (var row = 9; row <= 11; row++)
        {
            foreach (var column in new[] { 1, 2, 3, 4, 5 })
            {
                Assert.Equal(
                    XLBorderStyleValues.None,
                    sheet.Cell(row, column).Style.Border.BottomBorder);
            }
        }

        Assert.Equal(XLBorderStyleValues.Thin, sheet.Cell("A12").Style.Border.BottomBorder);
        Assert.Equal(XLBorderStyleValues.Thin, sheet.Cell("E12").Style.Border.BottomBorder);

        // No InsideBorder/OutsideBorder blanket was ever applied over the
        // data area — the header row (4) alone keeps its own full thin
        // border, distinct from the constructive per-edge data-area borders.
        Assert.Equal(XLBorderStyleValues.Thin, sheet.Cell("A4").Style.Border.TopBorder);
        Assert.Equal(XLBorderStyleValues.Thin, sheet.Cell("A4").Style.Border.BottomBorder);
        Assert.Equal(XLBorderStyleValues.Thin, sheet.Cell("E4").Style.Border.RightBorder);
    }

    [Fact]
    public async Task Export_sets_biz_udpgothic_font_on_title_and_data_cells()
    {
        using var temporary = new TemporaryDirectory();
        var exporter = new ClosedXmlWeeklyReportExporter();
        var range = new WeekRange(new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 2));
        var rows = new[]
        {
            new ReportRow(new DateOnly(2026, 7, 28), "手動メモ", "最初", "")
        };

        var path = await exporter.ExportAsync(
            range,
            rows,
            temporary.Path,
            new ReportIdentity("サンプル株式会社", "山田 太郎"));

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet("業務週報");

        Assert.Equal("BIZ UDPゴシック", sheet.Cell("A1").Style.Font.FontName);
        Assert.Equal("BIZ UDPゴシック", sheet.Cell("A5").Style.Font.FontName);
        Assert.Equal("BIZ UDPゴシック", sheet.Cell("D5").Style.Font.FontName);
    }

    [Fact]
    public async Task Export_uses_custom_report_title_in_file_name_and_title_cell()
    {
        using var temporary = new TemporaryDirectory();
        var exporter = new ClosedXmlWeeklyReportExporter();
        var range = new WeekRange(new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 2));
        var rows = new[]
        {
            new ReportRow(new DateOnly(2026, 7, 28), "手動メモ", "最初", "")
        };

        var path = await exporter.ExportAsync(
            range,
            rows,
            temporary.Path,
            new ReportIdentity("サンプル株式会社", "山田 太郎", "カスタム週報"));

        Assert.Equal(
            "カスタム週報 20260727-20260802.xlsx",
            Path.GetFileName(path));
        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet("業務週報");
        Assert.Equal("カスタム週報 2026/07/27〜2026/08/02", sheet.Cell("A1").GetString());
    }

    [Theory]
    [InlineData("業務週報", "業務週報")]
    [InlineData("  ", "業務週報")]
    [InlineData("", "業務週報")]
    [InlineData(null, "業務週報")]
    [InlineData("報告書/週次/業務", "報告書_週次_業務")]
    public void ReportFileNameSanitizer_replaces_invalid_characters_and_falls_back_when_blank(
        string? title,
        string expected)
    {
        Assert.Equal(expected, ReportFileNameSanitizer.Sanitize(title));
    }

    [Fact]
    public void CreateFileName_sanitizes_configured_title_for_the_file_system()
    {
        var exporter = new ClosedXmlWeeklyReportExporter();
        var range = new WeekRange(new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 2));

        var fileName = exporter.CreateFileName(
            range,
            new ReportIdentity("サンプル株式会社", "山田 太郎", "週報/7/27"));

        Assert.Equal("週報_7_27 20260727-20260802.xlsx", fileName);
    }

    [Fact]
    public async Task ExportMonthAsync_uses_the_monthly_filename_and_title_cell()
    {
        using var temporary = new TemporaryDirectory();
        var exporter = new ClosedXmlWeeklyReportExporter();
        var rows = new[]
        {
            new ReportRow(new DateOnly(2026, 7, 28), "案件A", "最初", "")
        };

        var path = await exporter.ExportMonthAsync(
            2026,
            7,
            rows,
            temporary.Path,
            new ReportIdentity("サンプル株式会社", "山田 太郎"));

        Assert.Equal("業務週報 月次 202607.xlsx", Path.GetFileName(path));
        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet("業務週報");
        Assert.Equal("業務週報 2026年7月 月次まとめ", sheet.Cell("A1").GetString());
    }

    [Fact]
    public void CreateMonthFileName_sanitizes_configured_title_for_the_file_system()
    {
        var exporter = new ClosedXmlWeeklyReportExporter();

        var fileName = exporter.CreateMonthFileName(
            2026,
            7,
            new ReportIdentity("サンプル株式会社", "山田 太郎", "週報/7/27"));

        Assert.Equal("週報_7_27 月次 202607.xlsx", fileName);
    }

    [Fact]
    public async Task ExportMonthAsync_groups_rows_from_different_weeks_by_calendar_day()
    {
        using var temporary = new TemporaryDirectory();
        var exporter = new ClosedXmlWeeklyReportExporter();
        var rows = new[]
        {
            // 2026-07-02 falls in a different week than 2026-07-30, but both are
            // in July — the monthly export must group them by day regardless.
            new ReportRow(new DateOnly(2026, 7, 2), "案件A", "月初の活動", ""),
            new ReportRow(new DateOnly(2026, 7, 30), "案件B", "月末の活動その1", "完了"),
            new ReportRow(new DateOnly(2026, 7, 30), "案件C", "月末の活動その2", "")
        };

        var path = await exporter.ExportMonthAsync(
            2026,
            7,
            rows,
            temporary.Path,
            new ReportIdentity("サンプル株式会社", "山田 太郎"));

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet("業務週報");

        Assert.Equal("2026/07/02", sheet.Cell("A5").GetString());
        Assert.Equal("(木)", sheet.Cell("B5").GetString());
        Assert.Equal("① 月初の活動", sheet.Cell("D5").GetString());

        Assert.Equal(string.Empty, sheet.Cell("A6").GetString());
        Assert.Equal(string.Empty, sheet.Cell("A7").GetString());
        Assert.Equal(string.Empty, sheet.Cell("A8").GetString());

        // 7/30 has two items -> two item rows (9-10), each item's 活動内容/結果
        // on its own row, then blanks padding to the uniform minimum of 4
        // rows (11-12).
        Assert.Equal("2026/07/30", sheet.Cell("A9").GetString());
        Assert.Equal("(木)", sheet.Cell("B9").GetString());
        Assert.Equal("① 月末の活動その1", sheet.Cell("D9").GetString());
        Assert.Equal("① 完了", sheet.Cell("E9").GetString());

        Assert.Equal(string.Empty, sheet.Cell("A10").GetString());
        Assert.Equal("② 月末の活動その2", sheet.Cell("D10").GetString());
        Assert.Equal(string.Empty, sheet.Cell("E10").GetString());

        Assert.Equal(string.Empty, sheet.Cell("A11").GetString());
        Assert.Equal(string.Empty, sheet.Cell("A12").GetString());
    }
}
