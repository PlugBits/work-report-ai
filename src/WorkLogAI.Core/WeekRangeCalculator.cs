namespace WorkLogAI.Core;

public sealed class WeekRangeCalculator
{
    public WeekRangeCalculator(DayOfWeek weekStartsOn = DayOfWeek.Monday)
    {
        WeekStartsOn = weekStartsOn;
    }

    public DayOfWeek WeekStartsOn { get; }

    public WeekRange GetWeekRange(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek - (int)WeekStartsOn + 7) % 7;
        var start = date.AddDays(-offset);
        return new WeekRange(start, start.AddDays(6));
    }
}
