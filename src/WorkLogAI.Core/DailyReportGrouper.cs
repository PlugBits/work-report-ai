namespace WorkLogAI.Core;

/// <summary>
/// One numbered item within a <see cref="DailyReportRow"/>: the day's work item,
/// activity, and (optionally blank) result/next-step text, paired with the shared
/// circled-number label used to cross-reference the three columns of the exported
/// report.
/// </summary>
public sealed record DailyReportItem(
    int Number,
    string WorkItem,
    string Activity,
    string ResultOrNext);

/// <summary>
/// All the selected <see cref="ReportRow"/>s for a single calendar day, numbered in
/// arrival order for cross-referencing across the 項目/活動内容/結果 columns of the
/// exported report.
/// </summary>
public sealed record DailyReportRow(DateOnly Date, IReadOnlyList<DailyReportItem> Items);

/// <summary>
/// Groups the flat, already-filtered <see cref="ReportRow"/> sequence used by the
/// Excel exporter into one row per calendar day, numbering each day's items with
/// circled digits (①②③…) so the 項目, 活動内容, and 結果・決定事項 columns can be
/// cross-referenced by number, matching the user's real submitted report format.
/// </summary>
public static class DailyReportGrouper
{
    /// <summary>
    /// Groups <paramref name="rows"/> by <see cref="ReportRow.Date"/>, preserving the
    /// input's relative order both across days (the day of the first row seen for a
    /// given date determines that day's position) and within a day (stable order of
    /// arrival), and numbering each day's items starting at 1.
    /// </summary>
    public static IReadOnlyList<DailyReportRow> Group(IEnumerable<ReportRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var order = new List<DateOnly>();
        var byDate = new Dictionary<DateOnly, List<ReportRow>>();
        foreach (var row in rows)
        {
            if (!byDate.TryGetValue(row.Date, out var bucket))
            {
                bucket = [];
                byDate[row.Date] = bucket;
                order.Add(row.Date);
            }

            bucket.Add(row);
        }

        var result = new List<DailyReportRow>(order.Count);
        foreach (var date in order)
        {
            var bucket = byDate[date];
            var items = new List<DailyReportItem>(bucket.Count);
            for (var index = 0; index < bucket.Count; index++)
            {
                var row = bucket[index];
                items.Add(new DailyReportItem(index + 1, row.WorkItem, row.Activity, row.ResultOrNext));
            }

            result.Add(new DailyReportRow(date, items));
        }

        return result;
    }

    private static readonly string[] CircledDigits =
    [
        "①", "②", "③", "④", "⑤", "⑥", "⑦", "⑧", "⑨", "⑩",
        "⑪", "⑫", "⑬", "⑭", "⑮", "⑯", "⑰", "⑱", "⑲", "⑳"
    ];

    /// <summary>
    /// Renders <paramref name="number"/> (1-based) as a circled digit (①..⑳ for
    /// 1..20); numbers outside that range fall back to a plain parenthesized form,
    /// e.g. "(21)".
    /// </summary>
    public static string CircledNumber(int number)
    {
        if (number is >= 1 and <= 20)
        {
            return CircledDigits[number - 1];
        }

        return $"({number})";
    }
}
