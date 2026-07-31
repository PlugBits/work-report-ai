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
        Assert.Equal("サンプル株式会社", sheet.Cell("D1").GetString());
        Assert.Equal("山田 太郎", sheet.Cell("D2").GetString());
        Assert.Equal(
            new[] { "日時", "項目/案件・目標金額", "活動内容", "結果・決定事項/今後の課題" },
            sheet.Range("A3:D3").Cells().Select(cell => cell.GetString()).ToArray());

        Assert.Equal("社内\n2026/07/28\n山田", sheet.Cell("A4").GetString());
        Assert.Equal("① 最初", sheet.Cell("C4").GetString());
        Assert.Equal("社内\n2026/07/30\n山田", sheet.Cell("A5").GetString());
        Assert.Equal("① 二番目", sheet.Cell("C5").GetString());

        Assert.All(
            sheet.Range("A3:D5").Cells(),
            cell => Assert.True(cell.Style.Alignment.WrapText));
        Assert.Equal(XLPageOrientation.Landscape, sheet.PageSetup.PageOrientation);
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
            sheet.Range("A3:D3").Cells(),
            cell =>
            {
                Assert.True(cell.Style.Font.Bold);
                Assert.Equal(XLColor.White, cell.Style.Font.FontColor);
                Assert.Equal(XLColor.FromHtml("#2E74B5"), cell.Style.Fill.BackgroundColor);
            });
    }

    [Fact]
    public async Task Export_groups_multiple_selected_items_on_the_same_day_into_one_numbered_row()
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

        // Two calendar days -> two data rows, not three.
        Assert.Equal("社内\n2026/07/28\n山田", sheet.Cell("A4").GetString());
        Assert.Equal("① 案件A\n② 案件B", sheet.Cell("B4").GetString());
        Assert.Equal("① 最初の活動\n② 二番目の活動", sheet.Cell("C4").GetString());
        Assert.Equal("② 完了", sheet.Cell("D4").GetString());

        Assert.Equal("社内\n2026/07/30\n山田", sheet.Cell("A5").GetString());
        Assert.Equal("① 案件C", sheet.Cell("B5").GetString());
        Assert.Equal("① 三番目の活動", sheet.Cell("C5").GetString());
        Assert.Equal(string.Empty, sheet.Cell("D5").GetString());

        Assert.Equal(string.Empty, sheet.Cell("A6").GetString());
    }

    [Fact]
    public async Task Export_omits_surname_line_when_employee_name_is_blank()
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
            new ReportIdentity("サンプル株式会社", "  "));

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet("業務週報");

        Assert.Equal("社内\n2026/07/28", sheet.Cell("A4").GetString());
    }

    [Fact]
    public async Task Export_uses_only_first_token_of_employee_name_as_surname()
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
            new ReportIdentity("サンプル株式会社", "田中"));

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet("業務週報");

        Assert.Equal("社内\n2026/07/28\n田中", sheet.Cell("A4").GetString());
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
    public async Task Export_splits_a_mixed_day_into_separate_internal_and_external_rows()
    {
        using var temporary = new TemporaryDirectory();
        var exporter = new ClosedXmlWeeklyReportExporter();
        var range = new WeekRange(new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 2));
        var rows = new[]
        {
            new ReportRow(new DateOnly(2026, 7, 28), "社内案件", "社内活動", ""),
            new ReportRow(new DateOnly(2026, 7, 28), "社外案件", "社外活動", "", ReportCategories.External)
        };

        var path = await exporter.ExportAsync(
            range,
            rows,
            temporary.Path,
            new ReportIdentity("サンプル株式会社", "山田 太郎"));

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet("業務週報");

        Assert.Equal("社内\n2026/07/28\n山田", sheet.Cell("A4").GetString());
        Assert.Equal("① 社内案件", sheet.Cell("B4").GetString());
        Assert.Equal("社外\n2026/07/28\n山田", sheet.Cell("A5").GetString());
        Assert.Equal("① 社外案件", sheet.Cell("B5").GetString());
        Assert.Equal(string.Empty, sheet.Cell("A6").GetString());
    }

    [Fact]
    public async Task Export_sets_noto_sans_jp_font_on_title_and_data_cells()
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

        Assert.Equal("Noto Sans JP", sheet.Cell("A1").Style.Font.FontName);
        Assert.Equal("Noto Sans JP", sheet.Cell("A4").Style.Font.FontName);
        Assert.Equal("Noto Sans JP", sheet.Cell("C4").Style.Font.FontName);
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
}
