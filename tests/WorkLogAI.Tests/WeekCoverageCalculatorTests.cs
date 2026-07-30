using WorkLogAI.Core;

namespace WorkLogAI.Tests;

public sealed class WeekCoverageCalculatorTests
{
    private static readonly WeekRange MondayStartWeek = new(new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 2));

    [Fact]
    public void Empty_week_reports_all_seven_days_with_zero_candidates()
    {
        var days = WeekCoverageCalculator.Calculate(MondayStartWeek, []);

        Assert.Equal(7, days.Count);
        Assert.All(days, day => Assert.Equal(0, day.CandidateCount));
        Assert.All(days, day => Assert.False(day.HasCandidates));
    }

    [Fact]
    public void Days_are_returned_in_week_order_starting_at_range_start()
    {
        var days = WeekCoverageCalculator.Calculate(MondayStartWeek, []);

        Assert.Equal(MondayStartWeek.Start, days[0].Date);
        for (var i = 0; i < 7; i++)
        {
            Assert.Equal(MondayStartWeek.Start.AddDays(i), days[i].Date);
        }
        Assert.Equal(MondayStartWeek.End, days[6].Date);
    }

    [Fact]
    public void Partial_coverage_marks_only_days_with_candidates()
    {
        var candidateDates = new[]
        {
            new DateOnly(2026, 7, 27),
            new DateOnly(2026, 7, 29)
        };

        var days = WeekCoverageCalculator.Calculate(MondayStartWeek, candidateDates);

        Assert.Equal(1, days[0].CandidateCount);
        Assert.True(days[0].HasCandidates);
        Assert.Equal(0, days[1].CandidateCount);
        Assert.Equal(1, days[2].CandidateCount);
        Assert.Equal(0, days[3].CandidateCount);
        Assert.Equal(0, days[4].CandidateCount);
        Assert.Equal(0, days[5].CandidateCount);
        Assert.Equal(0, days[6].CandidateCount);
    }

    [Fact]
    public void Candidates_outside_the_range_are_ignored()
    {
        var candidateDates = new[]
        {
            MondayStartWeek.Start.AddDays(-1),
            MondayStartWeek.End.AddDays(1),
            new DateOnly(2020, 1, 1)
        };

        var days = WeekCoverageCalculator.Calculate(MondayStartWeek, candidateDates);

        Assert.All(days, day => Assert.Equal(0, day.CandidateCount));
    }

    [Fact]
    public void Counts_aggregate_multiple_candidates_on_the_same_day()
    {
        var candidateDates = new[]
        {
            new DateOnly(2026, 7, 28),
            new DateOnly(2026, 7, 28),
            new DateOnly(2026, 7, 28)
        };

        var days = WeekCoverageCalculator.Calculate(MondayStartWeek, candidateDates);

        Assert.Equal(3, days[1].CandidateCount);
        Assert.True(days[1].HasCandidates);
    }

    [Fact]
    public void Weekday_flag_is_correct_for_a_monday_starting_week()
    {
        var days = WeekCoverageCalculator.Calculate(MondayStartWeek, []);

        Assert.Equal(DayOfWeek.Monday, days[0].DayOfWeek);
        Assert.True(days[0].IsWeekday);
        Assert.True(days[1].IsWeekday);
        Assert.True(days[2].IsWeekday);
        Assert.True(days[3].IsWeekday);
        Assert.True(days[4].IsWeekday);
        Assert.Equal(DayOfWeek.Saturday, days[5].DayOfWeek);
        Assert.False(days[5].IsWeekday);
        Assert.Equal(DayOfWeek.Sunday, days[6].DayOfWeek);
        Assert.False(days[6].IsWeekday);
    }

    [Fact]
    public void Weekday_flag_is_correct_for_a_sunday_starting_week()
    {
        var sundayStartWeek = new WeekRange(new DateOnly(2026, 7, 26), new DateOnly(2026, 8, 1));

        var days = WeekCoverageCalculator.Calculate(sundayStartWeek, []);

        Assert.Equal(DayOfWeek.Sunday, days[0].DayOfWeek);
        Assert.False(days[0].IsWeekday);
        Assert.Equal(DayOfWeek.Monday, days[1].DayOfWeek);
        Assert.True(days[1].IsWeekday);
        Assert.Equal(DayOfWeek.Tuesday, days[2].DayOfWeek);
        Assert.True(days[2].IsWeekday);
        Assert.Equal(DayOfWeek.Wednesday, days[3].DayOfWeek);
        Assert.True(days[3].IsWeekday);
        Assert.Equal(DayOfWeek.Thursday, days[4].DayOfWeek);
        Assert.True(days[4].IsWeekday);
        Assert.Equal(DayOfWeek.Friday, days[5].DayOfWeek);
        Assert.True(days[5].IsWeekday);
        Assert.Equal(DayOfWeek.Saturday, days[6].DayOfWeek);
        Assert.False(days[6].IsWeekday);
    }
}
