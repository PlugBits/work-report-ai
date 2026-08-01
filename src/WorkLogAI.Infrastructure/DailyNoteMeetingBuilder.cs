using WorkLogAI.Core;

namespace WorkLogAI.Infrastructure;

/// <summary>
/// Builds the day's <see cref="DailyNoteMeeting"/> list for the Obsidian vault sync,
/// reusing the exact same base-name-plus-collision-suffix logic
/// (<see cref="MeetingFileNameBuilder"/>) that <see cref="MeetingMarkdownWriter"/> uses
/// when a meeting's Markdown is actually exported — but resolving collisions purely
/// against the sessions being rendered for this one day, in <c>started_at</c> order,
/// without touching disk. This reproduces the real exported filename as long as the
/// same-day, same-title sessions were themselves exported in <c>started_at</c> order
/// and no unrelated file with the same generated name exists in the meeting output
/// folder — a documented, accepted limitation (no meeting-to-file mapping is
/// persisted anywhere, so this is the closest reconstruction available without one).
/// </summary>
public static class DailyNoteMeetingBuilder
{
    public static IReadOnlyList<DailyNoteMeeting> Build(DateOnly date, IReadOnlyList<MeetingSession> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<DailyNoteMeeting>(sessions.Count);
        foreach (var session in sessions.OrderBy(session => session.StartedAt))
        {
            var baseName = MeetingFileNameBuilder.BuildBaseName(date, session.Title);
            var fileNameWithExtension = MeetingFileNameBuilder.NextAvailableFileName(
                baseName, ".md", candidate => used.Contains(candidate));
            used.Add(fileNameWithExtension);
            result.Add(new DailyNoteMeeting(session.Title, fileNameWithExtension[..^".md".Length]));
        }

        return result;
    }
}
