namespace WorkLogAI.Core;

public sealed record ReminderPlanInput(
    bool Enabled,
    TimeOnly ConfiguredTime,
    DateTime Now,
    int TodayNoteCount,
    DateOnly? LastShownDate);

public static class ReminderPlanner
{
    public static bool ShouldRemind(ReminderPlanInput input)
    {
        if (!input.Enabled)
        {
            return false;
        }

        if (input.Now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return false;
        }

        if (TimeOnly.FromDateTime(input.Now) < input.ConfiguredTime)
        {
            return false;
        }

        if (input.TodayNoteCount != 0)
        {
            return false;
        }

        var today = DateOnly.FromDateTime(input.Now);
        if (input.LastShownDate == today)
        {
            return false;
        }

        return true;
    }
}
