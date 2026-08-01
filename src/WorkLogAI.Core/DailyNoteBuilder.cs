using System.Globalization;
using System.Text;

namespace WorkLogAI.Core;

/// <summary>
/// A meeting closed/formatted on a given day, ready to be linked from that day's
/// daily note. <see cref="FileName"/> is the exported Markdown file name without
/// its extension (see <see cref="MeetingFileNameBuilder"/> in Infrastructure) —
/// the daily note links to it as <c>[[FileName|Title]]</c> so the link displays the
/// meeting's title, falling back to a bare <c>[[FileName]]</c> when
/// <see cref="Title"/> is blank.
/// </summary>
public sealed record DailyNoteMeeting(string Title, string FileName);

/// <summary>
/// Pure Markdown rendering for one day's Obsidian daily note. No IO — callers own
/// writing the result to disk (see DailyNoteWriter in Infrastructure). Every
/// section is omitted when its source list is empty; if the whole day has nothing
/// to show, <see cref="Build"/> returns <c>null</c> so the caller can skip writing
/// a file entirely.
/// </summary>
public static class DailyNoteBuilder
{
    private static readonly string[] WeekdayKanji = ["日", "月", "火", "水", "木", "金", "土"];

    /// <param name="date">The calendar day this note covers.</param>
    /// <param name="quickNotes">That day's quick notes; rendered in ascending time order.</param>
    /// <param name="meetings">Meetings closed/formatted that day.</param>
    /// <param name="gitEvents">Git source events for that day (only <see cref="SourceEvent.Title"/>
    /// and <see cref="SourceEvent.SourceRef"/> are used); grouped by the repository's path
    /// last segment, extracted from a <c>git:{repo}:{hash}</c> ref.</param>
    /// <param name="candidateLines">Adopted (selected) report-candidate lines for the day.</param>
    /// <param name="linkText">Applied to quick-note text and candidate activity text to
    /// resolve entity wikilinks (see <see cref="EntityLinker.Link"/>); meeting titles are
    /// never passed through this — they render as-is inside the meeting's
    /// <c>[[FileName|Title]]</c> link (see <see cref="DailyNoteMeeting"/>).</param>
    public static string? Build(
        DateOnly date,
        IReadOnlyList<QuickNote> quickNotes,
        IReadOnlyList<DailyNoteMeeting> meetings,
        IReadOnlyList<SourceEvent> gitEvents,
        IReadOnlyList<(string WorkItem, string Activity)> candidateLines,
        Func<string, string> linkText)
    {
        ArgumentNullException.ThrowIfNull(quickNotes);
        ArgumentNullException.ThrowIfNull(meetings);
        ArgumentNullException.ThrowIfNull(gitEvents);
        ArgumentNullException.ThrowIfNull(candidateLines);
        ArgumentNullException.ThrowIfNull(linkText);

        if (quickNotes.Count == 0 && meetings.Count == 0 && gitEvents.Count == 0 && candidateLines.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.Append("---").Append('\n');
        builder.Append("date: ").Append(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("tags: [worklog, daily]").Append('\n');
        builder.Append("---").Append('\n').Append('\n');
        builder.Append("# ").Append(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Append(" (").Append(WeekdayKanji[(int)date.DayOfWeek]).Append(")").Append('\n');

        AppendMemoSection(builder, quickNotes, linkText);
        AppendMeetingSection(builder, meetings);
        AppendDevelopmentSection(builder, gitEvents);
        AppendWeeklyReportSection(builder, candidateLines, linkText);

        return builder.ToString().TrimEnd('\n') + "\n";
    }

    private static void AppendMemoSection(
        StringBuilder builder,
        IReadOnlyList<QuickNote> quickNotes,
        Func<string, string> linkText)
    {
        if (quickNotes.Count == 0)
        {
            return;
        }

        builder.Append('\n').Append("## メモ").Append('\n');
        foreach (var note in quickNotes.OrderBy(note => note.CreatedAt))
        {
            builder.Append("- ")
                .Append(note.CreatedAt.ToString("HH:mm", CultureInfo.InvariantCulture))
                .Append(' ')
                .Append(linkText(note.Text))
                .Append('\n');
        }
    }

    private static void AppendMeetingSection(StringBuilder builder, IReadOnlyList<DailyNoteMeeting> meetings)
    {
        if (meetings.Count == 0)
        {
            return;
        }

        builder.Append('\n').Append("## 会議").Append('\n');
        foreach (var meeting in meetings)
        {
            builder.Append("- [[").Append(meeting.FileName);
            if (!string.IsNullOrWhiteSpace(meeting.Title))
            {
                builder.Append('|').Append(meeting.Title);
            }
            builder.Append("]]").Append('\n');
        }
    }

    private static void AppendDevelopmentSection(StringBuilder builder, IReadOnlyList<SourceEvent> gitEvents)
    {
        if (gitEvents.Count == 0)
        {
            return;
        }

        builder.Append('\n').Append("## 開発").Append('\n');
        foreach (var group in GroupByRepository(gitEvents))
        {
            builder.Append("- ").Append(group.Repository).Append(": コミット")
                .Append(group.Commits.Count).Append("件 — ")
                .Append(group.Commits[0]);
            if (group.Commits.Count > 1)
            {
                builder.Append(" ほか");
            }
            builder.Append('\n');
        }
    }

    private static void AppendWeeklyReportSection(
        StringBuilder builder,
        IReadOnlyList<(string WorkItem, string Activity)> candidateLines,
        Func<string, string> linkText)
    {
        if (candidateLines.Count == 0)
        {
            return;
        }

        builder.Append('\n').Append("## 週報").Append('\n');
        foreach (var (workItem, activity) in candidateLines)
        {
            builder.Append("- ").Append(workItem).Append(": ").Append(linkText(activity)).Append('\n');
        }
    }

    private static IReadOnlyList<(string Repository, IReadOnlyList<string> Commits)> GroupByRepository(
        IReadOnlyList<SourceEvent> gitEvents)
    {
        var order = new List<string>();
        var byRepository = new Dictionary<string, List<string>>();
        foreach (var gitEvent in gitEvents)
        {
            var repository = ExtractRepositoryDisplayName(gitEvent.SourceRef);
            if (!byRepository.TryGetValue(repository, out var commits))
            {
                commits = [];
                byRepository[repository] = commits;
                order.Add(repository);
            }

            commits.Add(gitEvent.Title);
        }

        return order.Select(repository => (repository, (IReadOnlyList<string>)byRepository[repository])).ToArray();
    }

    /// <summary>Extracts the repository's path last segment from a
    /// <c>git:{repo}:{hash}</c> source ref. The repo path itself may contain ':'
    /// (a Windows drive letter), so only the final ':' separates it from the hash.</summary>
    private static string ExtractRepositoryDisplayName(string sourceRef)
    {
        const string prefix = "git:";
        if (string.IsNullOrEmpty(sourceRef) || !sourceRef.StartsWith(prefix, StringComparison.Ordinal))
        {
            return sourceRef;
        }

        var remainder = sourceRef[prefix.Length..];
        var lastColon = remainder.LastIndexOf(':');
        var repositoryPath = lastColon >= 0 ? remainder[..lastColon] : remainder;
        var trimmed = repositoryPath.TrimEnd('/', '\\');
        var lastSeparator = trimmed.LastIndexOfAny(['/', '\\']);
        var lastSegment = lastSeparator >= 0 ? trimmed[(lastSeparator + 1)..] : trimmed;
        return lastSegment.Length > 0 ? lastSegment : repositoryPath;
    }
}
