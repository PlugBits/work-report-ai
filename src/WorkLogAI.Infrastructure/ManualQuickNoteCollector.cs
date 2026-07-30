using WorkLogAI.Core;

namespace WorkLogAI.Infrastructure;

public sealed class ManualQuickNoteCollector(IQuickNoteRepository notes) : ISourceCollector
{
    public string SourceName => "手動メモ";

    public async Task<SourceCollectionResult> CollectAsync(
        WeekRange range,
        CancellationToken cancellationToken = default)
    {
        var bounds = WeekRangeTimeBounds.Local(range);
        var weeklyNotes = await notes.ListAsync(bounds.From, bounds.To, cancellationToken: cancellationToken);
        var events = weeklyNotes.Select(note => SourceEventFactory.Create(
            note.CreatedAt,
            SourceTypes.Manual,
            "手動メモ",
            note.Text,
            "ユーザーがクイック入力で記録",
            $"quick-note:{note.Id:D}",
            1.0)).ToArray();
        return new SourceCollectionResult(SourceName, events, []);
    }
}
