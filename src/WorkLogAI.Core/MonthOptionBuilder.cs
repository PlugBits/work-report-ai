namespace WorkLogAI.Core;

public readonly record struct MonthOption(int Year, int Month);

/// <summary>
/// Builds the ordered list of selectable months for the monthly summary export
/// picker: the current month first, followed by the previous months, newest to
/// oldest, wrapping the year boundary correctly (e.g. January rolls back into
/// December of the prior year).
/// </summary>
public static class MonthOptionBuilder
{
    public static IReadOnlyList<MonthOption> Build(DateOnly today, int count)
    {
        if (count <= 0)
        {
            return [];
        }

        var options = new List<MonthOption>(count);
        var cursor = new DateOnly(today.Year, today.Month, 1);
        for (var index = 0; index < count; index++)
        {
            options.Add(new MonthOption(cursor.Year, cursor.Month));
            cursor = cursor.AddMonths(-1);
        }

        return options;
    }
}
