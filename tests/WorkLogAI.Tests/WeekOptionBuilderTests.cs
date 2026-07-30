using WorkLogAI.Core;

namespace WorkLogAI.Tests;

public sealed class WeekOptionBuilderTests
{
    private static readonly DateOnly Today = new(2026, 7, 30);

    [Fact]
    public void Builds_the_requested_number_of_options()
    {
        var options = WeekOptionBuilder.Build(Today, DayOfWeek.Monday, 8);

        Assert.Equal(8, options.Count);
    }

    [Fact]
    public void First_option_is_this_week_and_second_is_last_week()
    {
        var options = WeekOptionBuilder.Build(Today, DayOfWeek.Monday, 8);

        Assert.Equal(WeekOptionKind.ThisWeek, options[0].Kind);
        Assert.Equal(WeekOptionKind.LastWeek, options[1].Kind);
        Assert.All(options.Skip(2), option => Assert.Equal(WeekOptionKind.Older, option.Kind));
    }

    [Fact]
    public void This_week_range_matches_the_week_range_calculator()
    {
        var expected = new WeekRangeCalculator(DayOfWeek.Monday).GetWeekRange(Today);

        var options = WeekOptionBuilder.Build(Today, DayOfWeek.Monday, 8);

        Assert.Equal(expected, options[0].Range);
    }

    [Fact]
    public void Options_are_ordered_newest_first_with_no_gaps_or_overlap()
    {
        var options = WeekOptionBuilder.Build(Today, DayOfWeek.Monday, 8);

        for (var i = 0; i < options.Count - 1; i++)
        {
            Assert.True(options[i].Range.Start > options[i + 1].Range.Start);
            Assert.Equal(options[i + 1].Range.Start, options[i].Range.Start.AddDays(-7));
            Assert.Equal(options[i].Range.Start.AddDays(6), options[i].Range.End);
        }
    }

    [Fact]
    public void Ranges_are_correct_across_a_month_boundary()
    {
        var earlyAugust = new DateOnly(2026, 8, 3);
        var options = WeekOptionBuilder.Build(earlyAugust, DayOfWeek.Monday, 8);

        Assert.Equal(new DateOnly(2026, 8, 3), options[0].Range.Start);
        Assert.Equal(new DateOnly(2026, 8, 9), options[0].Range.End);
        Assert.Equal(new DateOnly(2026, 7, 27), options[1].Range.Start);
        Assert.Equal(new DateOnly(2026, 8, 2), options[1].Range.End);
        Assert.Equal(new DateOnly(2026, 7, 6), options[4].Range.Start);
        Assert.Equal(new DateOnly(2026, 7, 12), options[4].Range.End);
    }

    [Fact]
    public void Honors_a_non_monday_week_start()
    {
        var options = WeekOptionBuilder.Build(Today, DayOfWeek.Sunday, 8);

        Assert.Equal(new DateOnly(2026, 7, 26), options[0].Range.Start);
        Assert.Equal(new DateOnly(2026, 8, 1), options[0].Range.End);
    }

    [Fact]
    public void Zero_count_returns_no_options()
    {
        var options = WeekOptionBuilder.Build(Today, DayOfWeek.Monday, 0);

        Assert.Empty(options);
    }
}
