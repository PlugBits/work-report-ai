using WorkLogAI.Core;

namespace WorkLogAI.Tests;

public sealed class MonthOptionBuilderTests
{
    private static readonly DateOnly Today = new(2026, 7, 30);

    [Fact]
    public void Builds_twelve_entries_by_default_request()
    {
        var options = MonthOptionBuilder.Build(Today, 12);

        Assert.Equal(12, options.Count);
    }

    [Fact]
    public void First_option_is_the_current_month()
    {
        var options = MonthOptionBuilder.Build(Today, 12);

        Assert.Equal(new MonthOption(2026, 7), options[0]);
    }

    [Fact]
    public void Options_are_ordered_newest_first_with_no_gaps()
    {
        var options = MonthOptionBuilder.Build(Today, 12);

        for (var i = 0; i < options.Count - 1; i++)
        {
            var current = options[i];
            var previous = options[i + 1];
            var currentIndex = (current.Year * 12) + current.Month;
            var previousIndex = (previous.Year * 12) + previous.Month;
            Assert.Equal(1, currentIndex - previousIndex);
        }
    }

    [Fact]
    public void Wraps_the_year_boundary_correctly()
    {
        var january = new DateOnly(2026, 1, 15);
        var options = MonthOptionBuilder.Build(january, 3);

        Assert.Equal(new MonthOption(2026, 1), options[0]);
        Assert.Equal(new MonthOption(2025, 12), options[1]);
        Assert.Equal(new MonthOption(2025, 11), options[2]);
    }

    [Fact]
    public void Zero_count_returns_no_options()
    {
        var options = MonthOptionBuilder.Build(Today, 0);

        Assert.Empty(options);
    }
}
