using WorkLogAI.Core;
using WorkLogAI.Infrastructure;

namespace WorkLogAI.Tests;

public sealed class DailyNoteMeetingBuilderTests
{
    private static readonly DateOnly Date = new(2026, 7, 31);

    [Fact]
    public void No_sessions_returns_an_empty_list()
    {
        var result = DailyNoteMeetingBuilder.Build(Date, []);

        Assert.Empty(result);
    }

    [Fact]
    public void A_single_session_gets_the_plain_base_name_with_no_suffix()
    {
        var session = Session("09:00", "定例会議");

        var result = DailyNoteMeetingBuilder.Build(Date, [session]);

        var meeting = Assert.Single(result);
        Assert.Equal("定例会議", meeting.Title);
        Assert.Equal("2026-07-31_定例会議", meeting.FileName);
    }

    [Fact]
    public void Two_same_titled_sessions_the_same_day_get_a_numeric_suffix_in_started_at_order()
    {
        var later = Session("15:00", "定例会議");
        var earlier = Session("09:00", "定例会議");

        // Passed out of order to verify the builder sorts by started_at itself.
        var result = DailyNoteMeetingBuilder.Build(Date, [later, earlier]);

        Assert.Equal(2, result.Count);
        Assert.Equal("2026-07-31_定例会議", result[0].FileName);
        Assert.Equal("2026-07-31_定例会議_2", result[1].FileName);
    }

    [Fact]
    public void Different_titled_sessions_never_collide()
    {
        var result = DailyNoteMeetingBuilder.Build(
            Date, [Session("09:00", "定例会議"), Session("11:00", "予算会議")]);

        Assert.Equal(["2026-07-31_定例会議", "2026-07-31_予算会議"], result.Select(m => m.FileName));
    }

    private static MeetingSession Session(string time, string title)
    {
        var parts = time.Split(':');
        var startedAt = new DateTimeOffset(
            Date.Year, Date.Month, Date.Day, int.Parse(parts[0]), int.Parse(parts[1]), 0, TimeSpan.FromHours(9));
        return new MeetingSession(
            Guid.NewGuid(), title, "", MeetingKind.Meeting, startedAt, startedAt, MeetingStatus.Formatted,
            startedAt);
    }
}
