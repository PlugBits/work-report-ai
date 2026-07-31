using WorkLogAI.Core;

namespace WorkLogAI.Tests;

public sealed class DailyReportGrouperTests
{
    [Fact]
    public void Group_returns_one_row_per_calendar_day_preserving_first_seen_order()
    {
        var rows = new[]
        {
            new ReportRow(new DateOnly(2026, 7, 30), "案件A", "活動A", ""),
            new ReportRow(new DateOnly(2026, 7, 28), "案件B", "活動B", ""),
            new ReportRow(new DateOnly(2026, 7, 30), "案件C", "活動C", "")
        };

        var result = DailyReportGrouper.Group(rows);

        Assert.Equal(2, result.Count);
        Assert.Equal(new DateOnly(2026, 7, 30), result[0].Date);
        Assert.Equal(new DateOnly(2026, 7, 28), result[1].Date);
    }

    [Fact]
    public void Group_numbers_items_within_a_day_starting_at_one_in_arrival_order()
    {
        var rows = new[]
        {
            new ReportRow(new DateOnly(2026, 7, 28), "案件A", "活動A", ""),
            new ReportRow(new DateOnly(2026, 7, 28), "案件B", "活動B", ""),
            new ReportRow(new DateOnly(2026, 7, 28), "案件C", "活動C", "")
        };

        var result = DailyReportGrouper.Group(rows);

        var day = Assert.Single(result);
        Assert.Equal(3, day.Items.Count);
        Assert.Equal([1, 2, 3], day.Items.Select(item => item.Number).ToArray());
        Assert.Equal(["案件A", "案件B", "案件C"], day.Items.Select(item => item.WorkItem).ToArray());
        Assert.Equal(["活動A", "活動B", "活動C"], day.Items.Select(item => item.Activity).ToArray());
    }

    [Fact]
    public void Group_preserves_stable_within_day_order_when_interleaved_with_other_days()
    {
        var rows = new[]
        {
            new ReportRow(new DateOnly(2026, 7, 28), "案件1", "活動1", ""),
            new ReportRow(new DateOnly(2026, 7, 29), "案件X", "活動X", ""),
            new ReportRow(new DateOnly(2026, 7, 28), "案件2", "活動2", ""),
            new ReportRow(new DateOnly(2026, 7, 29), "案件Y", "活動Y", "")
        };

        var result = DailyReportGrouper.Group(rows);

        Assert.Equal(2, result.Count);
        var first = result.Single(day => day.Date == new DateOnly(2026, 7, 28));
        Assert.Equal(["案件1", "案件2"], first.Items.Select(item => item.WorkItem).ToArray());
        var second = result.Single(day => day.Date == new DateOnly(2026, 7, 29));
        Assert.Equal(["案件X", "案件Y"], second.Items.Select(item => item.WorkItem).ToArray());
    }

    [Fact]
    public void Group_keeps_blank_and_non_blank_results_as_given_on_each_item()
    {
        var rows = new[]
        {
            new ReportRow(new DateOnly(2026, 7, 28), "案件A", "活動A", ""),
            new ReportRow(new DateOnly(2026, 7, 28), "案件B", "活動B", "完了")
        };

        var result = DailyReportGrouper.Group(rows);

        var day = Assert.Single(result);
        Assert.Equal(string.Empty, day.Items[0].ResultOrNext);
        Assert.Equal("完了", day.Items[1].ResultOrNext);

        // The exporter projects only non-blank results into the D column, keeping
        // the original item number — verified here via the record contents that
        // drive that projection.
        var withResults = day.Items.Where(item => !string.IsNullOrWhiteSpace(item.ResultOrNext)).ToArray();
        var withResult = Assert.Single(withResults);
        Assert.Equal(2, withResult.Number);
        Assert.Equal("完了", withResult.ResultOrNext);
    }

    [Fact]
    public void Group_returns_empty_list_for_empty_input()
    {
        var result = DailyReportGrouper.Group([]);

        Assert.Empty(result);
    }

    [Theory]
    [InlineData(1, "①")]
    [InlineData(9, "⑨")]
    [InlineData(10, "⑩")]
    [InlineData(20, "⑳")]
    public void CircledNumber_returns_circled_digit_for_one_through_twenty(int number, string expected)
    {
        Assert.Equal(expected, DailyReportGrouper.CircledNumber(number));
    }

    [Theory]
    [InlineData(21, "(21)")]
    [InlineData(30, "(30)")]
    public void CircledNumber_falls_back_to_parenthesized_form_beyond_twenty(int number, string expected)
    {
        Assert.Equal(expected, DailyReportGrouper.CircledNumber(number));
    }
}
