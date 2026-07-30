namespace WorkLogAI.Core;

public sealed class ManualReportMapper
{
    public ReportRow Map(QuickNote note)
    {
        ArgumentNullException.ThrowIfNull(note);

        // Phase 1 deliberately performs no inference. The note is copied verbatim and
        // the result column stays empty instead of inventing a completed outcome.
        return new ReportRow(
            DateOnly.FromDateTime(note.CreatedAt.DateTime),
            "手動メモ",
            note.Text,
            string.Empty);
    }
}
