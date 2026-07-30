using WorkLogAI.Core;

namespace WorkLogAI.App;

internal static class LocalWeekClock
{
    public static (DateTimeOffset From, DateTimeOffset To) ToTimestampRange(WeekRange range)
    {
        var fromLocal = range.Start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var toLocal = range.End.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return (
            new DateTimeOffset(fromLocal, TimeZoneInfo.Local.GetUtcOffset(fromLocal)),
            new DateTimeOffset(toLocal, TimeZoneInfo.Local.GetUtcOffset(toLocal)));
    }
}
