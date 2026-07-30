using ClosedXML.Excel;
using WorkLogAI.Core;
using WorkLogAI.Infrastructure;

namespace WorkLogAI.Tests;

public sealed class ExcelExportTests
{
    [Fact]
    public async Task Export_has_exact_japanese_layout_and_chronological_rows()
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
            new ReportIdentity("YAHATA USA", "太田 貴也"));

        Assert.Equal(
            "業務週報(USA太田) 20260727-20260802.xlsx",
            Path.GetFileName(path));
        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet("業務週報");
        Assert.Equal("業務週報(USA太田) 2026/07/27〜2026/08/02", sheet.Cell("A1").GetString());
        Assert.Equal("YAHATA USA / 太田 貴也", sheet.Cell("D1").GetString());
        Assert.Equal(
            new[] { "日時", "業務項目", "活動内容", "結果・決定事項／今後の課題" },
            sheet.Range("A3:D3").Cells().Select(cell => cell.GetString()).ToArray());
        Assert.Equal(new DateTime(2026, 7, 28), sheet.Cell("A4").GetDateTime());
        Assert.Equal("最初", sheet.Cell("C4").GetString());
        Assert.Equal(new DateTime(2026, 7, 30), sheet.Cell("A5").GetDateTime());
        Assert.Equal("二番目", sheet.Cell("C5").GetString());
        Assert.All(
            sheet.Range("A3:D5").Cells(),
            cell => Assert.True(cell.Style.Alignment.WrapText));
        Assert.Equal(XLPageOrientation.Landscape, sheet.PageSetup.PageOrientation);
        Assert.Equal(1, sheet.PageSetup.PagesWide);
    }

    [Fact]
    public async Task Export_merges_date_cells_for_consecutive_rows_sharing_a_date()
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
            new ReportIdentity("YAHATA USA", "太田 貴也"));

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet("業務週報");

        var dataMerge = Assert.Single(
            sheet.MergedRanges.Where(mergedRange => mergedRange.RangeAddress.ToString() == "A4:A5"));
        Assert.Equal(4, dataMerge.RangeAddress.FirstAddress.RowNumber);
        Assert.Equal(5, dataMerge.RangeAddress.LastAddress.RowNumber);
        Assert.Equal(new DateTime(2026, 7, 28), sheet.Cell("A4").GetDateTime());
        Assert.True(sheet.Cell("A4").IsMerged());
        Assert.True(sheet.Cell("A5").IsMerged());

        Assert.False(sheet.Cell("A6").IsMerged());
        Assert.Equal(new DateTime(2026, 7, 30), sheet.Cell("A6").GetDateTime());
        Assert.Equal("三番目", sheet.Cell("C6").GetString());
    }

    [Fact]
    public async Task Export_leaves_distinct_dates_unmerged()
    {
        using var temporary = new TemporaryDirectory();
        var exporter = new ClosedXmlWeeklyReportExporter();
        var range = new WeekRange(new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 2));
        var rows = new[]
        {
            new ReportRow(new DateOnly(2026, 7, 28), "手動メモ", "最初", ""),
            new ReportRow(new DateOnly(2026, 7, 29), "手動メモ", "二番目", ""),
            new ReportRow(new DateOnly(2026, 7, 30), "手動メモ", "三番目", "")
        };

        var path = await exporter.ExportAsync(
            range,
            rows,
            temporary.Path,
            new ReportIdentity("YAHATA USA", "太田 貴也"));

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet("業務週報");

        var dataAreaMerges = sheet.MergedRanges
            .Where(mergedRange => mergedRange.RangeAddress.FirstAddress.RowNumber >= 4)
            .ToArray();
        Assert.Empty(dataAreaMerges);

        // The title row merge (A1:C1) is unaffected by the data-row merging logic.
        Assert.Contains(
            sheet.MergedRanges,
            mergedRange => mergedRange.RangeAddress.ToString() == "A1:C1");

        Assert.False(sheet.Cell("A4").IsMerged());
        Assert.False(sheet.Cell("A5").IsMerged());
        Assert.False(sheet.Cell("A6").IsMerged());
    }
}
