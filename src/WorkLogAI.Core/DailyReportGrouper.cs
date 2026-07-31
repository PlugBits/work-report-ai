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
/// All the selected <see cref="ReportRow"/>s for a single calendar day and
/// 社内/社外 <see cref="ReportRow.Category"/>, numbered in arrival order for
/// cross-referencing across the 項目/活動内容/結果 columns of the exported report.
/// </summary>
public sealed record DailyReportRow(DateOnly Date, string Category, IReadOnlyList<DailyReportItem> Items);

/// <summary>
/// Groups the flat, already-filtered <see cref="ReportRow"/> sequence used by the
/// Excel exporter into one row per calendar day and 社内/社外 category, numbering
/// each group's items with circled digits (①②③…) so the 項目, 活動内容, and
/// 結果・決定事項 columns can be cross-referenced by number, matching the user's
/// real submitted report format. A day with both internal and external items
/// produces two rows — internal first, then external — each numbered
/// independently starting at 1.
/// </summary>
public static class DailyReportGrouper
{
    /// <summary>
    /// Groups <paramref name="rows"/> by (<see cref="ReportRow.Date"/>,
    /// <see cref="ReportRow.Category"/>), preserving the input's relative order
    /// both across groups (the group of the first row seen for a given date/
    /// category pair determines its position, with internal ordered before
    /// external within the same date) and within a group (stable order of
    /// arrival), and numbering each group's items starting at 1.
    /// </summary>
    public static IReadOnlyList<DailyReportRow> Group(IEnumerable<ReportRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var dateOrder = new List<DateOnly>();
        var categoriesByDate = new Dictionary<DateOnly, List<string>>();
        var byKey = new Dictionary<(DateOnly Date, string Category), List<ReportRow>>();
        foreach (var row in rows)
        {
            if (!categoriesByDate.TryGetValue(row.Date, out var categories))
            {
                categories = [];
                categoriesByDate[row.Date] = categories;
                dateOrder.Add(row.Date);
            }

            var key = (row.Date, row.Category);
            if (!byKey.TryGetValue(key, out var bucket))
            {
                bucket = [];
                byKey[key] = bucket;
                categories.Add(row.Category);
            }

            bucket.Add(row);
        }

        var result = new List<DailyReportRow>();
        foreach (var date in dateOrder)
        {
            // Preserve the date's position as first seen, but within a date
            // always emit internal before external regardless of arrival order.
            var categories = categoriesByDate[date].OrderBy(CategoryRank);
            foreach (var category in categories)
            {
                var bucket = byKey[(date, category)];
                var items = new List<DailyReportItem>(bucket.Count);
                for (var index = 0; index < bucket.Count; index++)
                {
                    var row = bucket[index];
                    items.Add(new DailyReportItem(index + 1, row.WorkItem, row.Activity, row.ResultOrNext));
                }

                result.Add(new DailyReportRow(date, category, items));
            }
        }

        return result;
    }

    private static int CategoryRank(string category) =>
        category == ReportCategories.External ? 1 : 0;

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
