using WorkLogAI.Core;

namespace WorkLogAI.Infrastructure;

/// <summary>
/// One-time-per-startup cleanup for report_candidates rows stored before
/// <see cref="GitEventText.StripFileList"/> covered merged rows (see
/// CandidateMergeService). Only the activity column is ever touched — edited,
/// selected, origin, and every other column are left exactly as stored. Idempotent
/// by construction: a row is only updated when stripping actually changes its text,
/// so a second run over already-cleaned rows updates nothing.
/// </summary>
public sealed class CandidateTextCleanup(IReportCandidateRepository candidates)
{
    private const string FileListMarker = "変更ファイル:";

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var dirty = await candidates.ListActivitiesContainingAsync(FileListMarker, cancellationToken);
        var updated = 0;
        foreach (var (id, activity) in dirty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stripped = GitEventText.StripFileList(activity);
            if (!string.Equals(stripped, activity, StringComparison.Ordinal))
            {
                await candidates.UpdateActivityAsync(id, stripped, cancellationToken);
                updated++;
            }
        }

        return updated;
    }
}
