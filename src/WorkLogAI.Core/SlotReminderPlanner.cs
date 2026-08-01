namespace WorkLogAI.Core;

/// <summary>
/// Input for <see cref="SlotReminderPlanner.SlotToFire"/>. <paramref name="SlotTimes"/> and
/// <paramref name="SlotLastShownDates"/> are parallel lists (same index refers to the same
/// slot) but need not arrive pre-sorted — the planner sorts the pairs by time defensively
/// before evaluating them, so the returned index always refers to chronological order.
/// </summary>
/// <param name="Enabled">Master on/off switch; mirrors <c>reminder.enabled</c>.</param>
/// <param name="SlotTimes">Configured slot times for the day, e.g. 11:00 and 16:00.</param>
/// <param name="Now">Current local date/time.</param>
/// <param name="SlotLastShownDates">
/// Per-slot last-shown date, parallel to <paramref name="SlotTimes"/>. A slot already shown
/// today is skipped for the rest of the day.
/// </param>
/// <param name="TodayNoteTimestamps">Today's non-deleted quick-note timestamps.</param>
/// <param name="SmartEnabled">Whether break-return acceleration is enabled.</param>
/// <param name="ReturnedFromBreak">
/// Whether a break-return signal (session unlock or idle recovery) was observed this tick.
/// </param>
public sealed record SlotReminderPlanInput(
    bool Enabled,
    IReadOnlyList<TimeOnly> SlotTimes,
    DateTime Now,
    IReadOnlyList<DateOnly?> SlotLastShownDates,
    IReadOnlyList<DateTimeOffset> TodayNoteTimestamps,
    bool SmartEnabled,
    bool ReturnedFromBreak);

/// <summary>
/// Decides which reminder slot (if any) should fire this tick. Each slot covers the half-day
/// since the previous slot's time (the first slot covers since midnight); a slot only fires
/// when nothing has been noted since its coverage start. Firing is either the slot's fixed
/// target time (which also acts as catch-up for a tick that only resumes after the target,
/// e.g. the PC was locked at the target time) or, when break-return acceleration is enabled,
/// up to 60 minutes early if the user has just returned from a break.
/// </summary>
public static class SlotReminderPlanner
{
    private static readonly TimeSpan SmartPreWindow = TimeSpan.FromMinutes(60);

    public static int? SlotToFire(SlotReminderPlanInput input)
    {
        if (!input.Enabled)
        {
            return null;
        }

        if (input.Now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return null;
        }

        if (input.SlotTimes.Count == 0)
        {
            return null;
        }

        var slots = new (TimeOnly Time, DateOnly? LastShown)[input.SlotTimes.Count];
        for (var i = 0; i < slots.Length; i++)
        {
            var lastShown = i < input.SlotLastShownDates.Count ? input.SlotLastShownDates[i] : null;
            slots[i] = (input.SlotTimes[i], lastShown);
        }

        Array.Sort(slots, (a, b) => a.Time.CompareTo(b.Time));

        var today = DateOnly.FromDateTime(input.Now);

        for (var i = 0; i < slots.Length; i++)
        {
            var (slotTime, lastShown) = slots[i];
            if (lastShown == today)
            {
                continue;
            }

            var coverageStartTime = i == 0 ? TimeOnly.MinValue : slots[i - 1].Time;
            var coverageStart = new DateTimeOffset(today.ToDateTime(coverageStartTime));
            var hasCoveringNote = input.TodayNoteTimestamps.Any(timestamp => timestamp >= coverageStart);
            if (hasCoveringNote)
            {
                continue;
            }

            var slotDateTime = today.ToDateTime(slotTime);
            if (input.Now >= slotDateTime)
            {
                return i;
            }

            if (input.SmartEnabled && input.ReturnedFromBreak)
            {
                var smartWindowStart = slotDateTime.AddMinutes(-SmartPreWindow.TotalMinutes);
                if (input.Now >= smartWindowStart)
                {
                    return i;
                }
            }
        }

        return null;
    }
}
