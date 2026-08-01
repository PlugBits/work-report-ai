using WorkLogAI.Core;

namespace WorkLogAI.Tests;

public sealed class SlotReminderPlannerTests
{
    private static readonly TimeOnly Slot0 = new(11, 0);
    private static readonly TimeOnly Slot1 = new(16, 0);
    private static readonly IReadOnlyList<TimeOnly> TwoSlots = [Slot0, Slot1];

    private static SlotReminderPlanInput Input(
        bool enabled = true,
        IReadOnlyList<TimeOnly>? slotTimes = null,
        DateTime? now = null,
        IReadOnlyList<DateOnly?>? lastShown = null,
        IReadOnlyList<DateTimeOffset>? notes = null,
        bool smartEnabled = false,
        bool returnedFromBreak = false) =>
        new(
            enabled,
            slotTimes ?? TwoSlots,
            now ?? new DateTime(2026, 7, 30, 11, 0, 0),
            lastShown ?? [null, null],
            notes ?? [],
            smartEnabled,
            returnedFromBreak);

    [Fact]
    public void Disabled_never_fires()
    {
        var input = Input(enabled: false, now: new DateTime(2026, 7, 30, 18, 0, 0));

        Assert.Null(SlotReminderPlanner.SlotToFire(input));
    }

    [Theory]
    [InlineData(2026, 8, 1)]
    [InlineData(2026, 8, 2)]
    public void Weekend_never_fires(int year, int month, int day)
    {
        var input = Input(now: new DateTime(year, month, day, 18, 0, 0));

        Assert.Null(SlotReminderPlanner.SlotToFire(input));
    }

    [Fact]
    public void Before_first_slot_target_with_no_notes_does_not_fire()
    {
        var input = Input(now: new DateTime(2026, 7, 30, 10, 59, 0));

        Assert.Null(SlotReminderPlanner.SlotToFire(input));
    }

    [Fact]
    public void At_first_slot_target_with_no_notes_fires_slot_zero()
    {
        var input = Input(now: new DateTime(2026, 7, 30, 11, 0, 0));

        Assert.Equal(0, SlotReminderPlanner.SlotToFire(input));
    }

    [Fact]
    public void Morning_memo_satisfies_slot_zero_but_slot_one_still_fires_after_its_target()
    {
        var notes = new DateTimeOffset[] { new(new DateTime(2026, 7, 30, 9, 0, 0)) };

        var beforeSlotOne = Input(now: new DateTime(2026, 7, 30, 15, 59, 0), notes: notes);
        Assert.Null(SlotReminderPlanner.SlotToFire(beforeSlotOne));

        var afterSlotOne = Input(now: new DateTime(2026, 7, 30, 16, 0, 0), notes: notes);
        Assert.Equal(1, SlotReminderPlanner.SlotToFire(afterSlotOne));
    }

    [Fact]
    public void Note_covering_both_slots_suppresses_both()
    {
        var notes = new DateTimeOffset[] { new(new DateTime(2026, 7, 30, 17, 0, 0)) };
        var input = Input(now: new DateTime(2026, 7, 30, 20, 0, 0), notes: notes);

        Assert.Null(SlotReminderPlanner.SlotToFire(input));
    }

    [Fact]
    public void Note_exactly_at_coverage_start_counts_as_covering()
    {
        // Slot 1's coverage starts exactly at slot 0's time (11:00); a note at exactly 11:00
        // covers both slot 0 (coverage start midnight) and slot 1 (coverage start 11:00).
        var notes = new DateTimeOffset[] { new(new DateTime(2026, 7, 30, 11, 0, 0)) };
        var input = Input(now: new DateTime(2026, 7, 30, 20, 0, 0), notes: notes);

        Assert.Null(SlotReminderPlanner.SlotToFire(input));
    }

    [Fact]
    public void Smart_fire_inside_the_sixty_minute_pre_window()
    {
        var input = Input(
            now: new DateTime(2026, 7, 30, 10, 30, 0),
            smartEnabled: true,
            returnedFromBreak: true);

        Assert.Equal(0, SlotReminderPlanner.SlotToFire(input));
    }

    [Fact]
    public void No_smart_fire_before_the_pre_window_opens()
    {
        var input = Input(
            now: new DateTime(2026, 7, 30, 9, 59, 0),
            smartEnabled: true,
            returnedFromBreak: true);

        Assert.Null(SlotReminderPlanner.SlotToFire(input));
    }

    [Fact]
    public void Smart_fire_at_the_exact_pre_window_boundary()
    {
        var input = Input(
            now: new DateTime(2026, 7, 30, 10, 0, 0),
            smartEnabled: true,
            returnedFromBreak: true);

        Assert.Equal(0, SlotReminderPlanner.SlotToFire(input));
    }

    [Fact]
    public void No_smart_fire_when_smart_disabled_even_if_returned_from_break()
    {
        var input = Input(
            now: new DateTime(2026, 7, 30, 10, 30, 0),
            smartEnabled: false,
            returnedFromBreak: true);

        Assert.Null(SlotReminderPlanner.SlotToFire(input));
    }

    [Fact]
    public void No_smart_fire_when_not_returned_from_break_even_if_smart_enabled()
    {
        var input = Input(
            now: new DateTime(2026, 7, 30, 10, 30, 0),
            smartEnabled: true,
            returnedFromBreak: false);

        Assert.Null(SlotReminderPlanner.SlotToFire(input));
    }

    [Fact]
    public void Catch_up_fires_after_target_even_without_smart_signal()
    {
        // Simulates the PC staying locked through the target time; the tick that resumes
        // afterwards should still fire, smart acceleration aside.
        var input = Input(now: new DateTime(2026, 7, 30, 13, 45, 0));

        Assert.Equal(0, SlotReminderPlanner.SlotToFire(input));
    }

    [Fact]
    public void Slot_already_shown_today_does_not_fire_again()
    {
        var today = new DateOnly(2026, 7, 30);
        var input = Input(
            now: new DateTime(2026, 7, 30, 18, 0, 0),
            lastShown: [today, null]);

        Assert.Equal(1, SlotReminderPlanner.SlotToFire(input));
    }

    [Fact]
    public void Both_slots_already_shown_today_fires_nothing()
    {
        var today = new DateOnly(2026, 7, 30);
        var input = Input(
            now: new DateTime(2026, 7, 30, 18, 0, 0),
            lastShown: [today, today]);

        Assert.Null(SlotReminderPlanner.SlotToFire(input));
    }

    [Fact]
    public void Previous_days_last_shown_date_reminds_again()
    {
        var yesterday = new DateOnly(2026, 7, 29);
        var input = Input(
            now: new DateTime(2026, 7, 30, 18, 0, 0),
            lastShown: [yesterday, yesterday]);

        Assert.Equal(0, SlotReminderPlanner.SlotToFire(input));
    }

    [Fact]
    public void Single_slot_list_fires_at_its_target()
    {
        var input = Input(
            slotTimes: [new TimeOnly(17, 0)],
            now: new DateTime(2026, 7, 30, 17, 0, 0),
            lastShown: [null]);

        Assert.Equal(0, SlotReminderPlanner.SlotToFire(input));
    }

    [Fact]
    public void Three_slot_list_fires_the_lowest_hungry_index()
    {
        var slots = new TimeOnly[] { new(9, 0), new(13, 0), new(17, 0) };
        var input = Input(
            slotTimes: slots,
            now: new DateTime(2026, 7, 30, 18, 0, 0),
            lastShown: [null, null, null]);

        Assert.Equal(0, SlotReminderPlanner.SlotToFire(input));
    }

    [Fact]
    public void Three_slot_list_skips_satisfied_and_already_shown_slots()
    {
        var slots = new TimeOnly[] { new(9, 0), new(13, 0), new(17, 0) };
        var today = new DateOnly(2026, 7, 30);
        var notes = new DateTimeOffset[] { new(new DateTime(2026, 7, 30, 10, 0, 0)) };
        var input = Input(
            slotTimes: slots,
            now: new DateTime(2026, 7, 30, 18, 0, 0),
            lastShown: [today, null, null],
            notes: notes);

        // Slot 0 already shown today; slot 1's coverage (since 09:00) is satisfied by the
        // 10:00 note; slot 2's coverage (since 13:00) has no note, so it fires.
        Assert.Equal(2, SlotReminderPlanner.SlotToFire(input));
    }

    [Fact]
    public void Unsorted_slot_input_is_sorted_defensively()
    {
        // Times and last-shown dates are parallel but given out of chronological order.
        var today = new DateOnly(2026, 7, 30);
        var slots = new TimeOnly[] { Slot1, Slot0 }; // 16:00 then 11:00
        var lastShown = new DateOnly?[] { null, today }; // 16:00-slot not shown, 11:00-slot shown

        var input = Input(
            slotTimes: slots,
            now: new DateTime(2026, 7, 30, 18, 0, 0),
            lastShown: lastShown);

        // Chronologically: slot index 0 is 11:00 (already shown today, per the paired
        // last-shown value), slot index 1 is 16:00 (not shown) and should fire.
        Assert.Equal(1, SlotReminderPlanner.SlotToFire(input));
    }
}
