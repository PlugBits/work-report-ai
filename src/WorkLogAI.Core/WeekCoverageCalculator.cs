namespace WorkLogAI.Core;

public readonly record struct DayCoverage(
    DateOnly Date,
    DayOfWeek DayOfWeek,
    int CandidateCount,
    bool IsWeekday)
{
    public bool HasCandidates => CandidateCount > 0;
}

public static class WeekCoverageCalculator
{
    public static IReadOnlyList<DayCoverage> Calculate(WeekRange range, IEnumerable<DateOnly> candidateDates)
    {
        var counts = new Dictionary<DateOnly, int>();
        foreach (var date in candidateDates)
        {
            if (!range.Contains(date))
            {
                continue;
            }

            counts[date] = counts.GetValueOrDefault(date) + 1;
        }

        var days = new List<DayCoverage>(7);
        for (var offset = 0; offset < 7; offset++)
        {
            var date = range.Start.AddDays(offset);
            var dayOfWeek = date.DayOfWeek;
            var isWeekday = dayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
            days.Add(new DayCoverage(date, dayOfWeek, counts.GetValueOrDefault(date), isWeekday));
        }

        return days;
    }
}
