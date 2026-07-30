using WorkLogAI.Core;

namespace WorkLogAI.Tests;

public sealed class ReminderPlannerTests
{
    private static readonly TimeOnly DefaultTime = new(17, 0);

    [Fact]
    public void Disabled_never_reminds()
    {
        var input = new ReminderPlanInput(
            Enabled: false,
            ConfiguredTime: DefaultTime,
            Now: new DateTime(2026, 7, 30, 18, 0, 0),
            TodayNoteCount: 0,
            LastShownDate: null);

        Assert.False(ReminderPlanner.ShouldRemind(input));
    }

    [Theory]
    [InlineData(2026, 8, 1)]
    [InlineData(2026, 8, 2)]
    public void Weekend_never_reminds(int year, int month, int day)
    {
        var input = new ReminderPlanInput(
            Enabled: true,
            ConfiguredTime: DefaultTime,
            Now: new DateTime(year, month, day, 18, 0, 0),
            TodayNoteCount: 0,
            LastShownDate: null);

        Assert.False(ReminderPlanner.ShouldRemind(input));
    }

    [Fact]
    public void Before_configured_time_does_not_remind()
    {
        var input = new ReminderPlanInput(
            Enabled: true,
            ConfiguredTime: DefaultTime,
            Now: new DateTime(2026, 7, 30, 16, 59, 0),
            TodayNoteCount: 0,
            LastShownDate: null);

        Assert.False(ReminderPlanner.ShouldRemind(input));
    }

    [Fact]
    public void At_configured_time_with_zero_notes_reminds()
    {
        var input = new ReminderPlanInput(
            Enabled: true,
            ConfiguredTime: DefaultTime,
            Now: new DateTime(2026, 7, 30, 17, 0, 0),
            TodayNoteCount: 0,
            LastShownDate: null);

        Assert.True(ReminderPlanner.ShouldRemind(input));
    }

    [Fact]
    public void After_configured_time_with_zero_notes_reminds()
    {
        var input = new ReminderPlanInput(
            Enabled: true,
            ConfiguredTime: DefaultTime,
            Now: new DateTime(2026, 7, 30, 20, 30, 0),
            TodayNoteCount: 0,
            LastShownDate: null);

        Assert.True(ReminderPlanner.ShouldRemind(input));
    }

    [Fact]
    public void Nonzero_note_count_suppresses_reminder()
    {
        var input = new ReminderPlanInput(
            Enabled: true,
            ConfiguredTime: DefaultTime,
            Now: new DateTime(2026, 7, 30, 18, 0, 0),
            TodayNoteCount: 1,
            LastShownDate: null);

        Assert.False(ReminderPlanner.ShouldRemind(input));
    }

    [Fact]
    public void Same_day_repeat_is_suppressed()
    {
        var input = new ReminderPlanInput(
            Enabled: true,
            ConfiguredTime: DefaultTime,
            Now: new DateTime(2026, 7, 30, 18, 0, 0),
            TodayNoteCount: 0,
            LastShownDate: new DateOnly(2026, 7, 30));

        Assert.False(ReminderPlanner.ShouldRemind(input));
    }

    [Fact]
    public void Next_day_reminds_again()
    {
        var input = new ReminderPlanInput(
            Enabled: true,
            ConfiguredTime: DefaultTime,
            Now: new DateTime(2026, 7, 31, 18, 0, 0),
            TodayNoteCount: 0,
            LastShownDate: new DateOnly(2026, 7, 30));

        Assert.True(ReminderPlanner.ShouldRemind(input));
    }
}
