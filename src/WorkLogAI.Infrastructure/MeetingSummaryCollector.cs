using WorkLogAI.Core;

namespace WorkLogAI.Infrastructure;

/// <summary>
/// Turns already-formatted meeting sessions into weekly source events. Only the
/// summary_line plus the session's title/time are ever carried into an event body —
/// raw meeting lines never leave <see cref="IMeetingRepository"/> through this path,
/// which is what guarantees they never reach the weekly AI generation prompt (see
/// AiPromptBuilder, which only ever sends SourceEvent title/body/evidence).
/// </summary>
public sealed class MeetingSummaryCollector(IMeetingRepository meetings) : ISourceCollector
{
    public string SourceName => "meeting";

    public async Task<SourceCollectionResult> CollectAsync(
        WeekRange range,
        CancellationToken cancellationToken = default)
    {
        var formatted = await meetings.ListFormattedInRangeAsync(range, cancellationToken);
        var events = formatted.Select(pair => SourceEventFactory.Create(
            pair.Session.StartedAt,
            SourceTypes.Meeting,
            pair.Session.Title,
            pair.Summary.SummaryLine,
            $"{pair.Session.StartedAt:yyyy-MM-dd HH:mm} {pair.Session.Title}",
            $"meeting:{pair.Session.Id:D}",
            0.8)).ToArray();
        return new SourceCollectionResult(SourceName, events, []);
    }
}
