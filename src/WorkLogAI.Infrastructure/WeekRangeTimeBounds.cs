using WorkLogAI.Core;

namespace WorkLogAI.Infrastructure;

public static class WeekRangeTimeBounds
{
    public static (DateTimeOffset From, DateTimeOffset To) Local(WeekRange range)
    {
        var from = range.Start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var to = range.End.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return (
            new DateTimeOffset(from, TimeZoneInfo.Local.GetUtcOffset(from)),
            new DateTimeOffset(to, TimeZoneInfo.Local.GetUtcOffset(to)));
    }
}
