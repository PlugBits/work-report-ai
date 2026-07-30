namespace WorkLogAI.Core;

public enum WeekOptionKind
{
    ThisWeek,
    LastWeek,
    Older
}

public readonly record struct WeekOption(WeekRange Range, WeekOptionKind Kind);

public static class WeekOptionBuilder
{
    public static IReadOnlyList<WeekOption> Build(DateOnly today, DayOfWeek weekStartsOn, int count)
    {
        if (count <= 0)
        {
            return [];
        }

        var calculator = new WeekRangeCalculator(weekStartsOn);
        var thisWeekStart = calculator.GetWeekRange(today).Start;
        var options = new List<WeekOption>(count);
        for (var index = 0; index < count; index++)
        {
            var range = calculator.GetWeekRange(thisWeekStart.AddDays(-7 * index));
            var kind = index switch
            {
                0 => WeekOptionKind.ThisWeek,
                1 => WeekOptionKind.LastWeek,
                _ => WeekOptionKind.Older
            };
            options.Add(new WeekOption(range, kind));
        }

        return options;
    }
}
