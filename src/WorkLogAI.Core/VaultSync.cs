using System.Text;

namespace WorkLogAI.Core;

/// <summary>
/// Outcome of one Obsidian vault sync run (tray **Obsidianへ同期…**). Mirrors the
/// Succeeded/Error convention used by <see cref="EntityExtractionResult"/> and
/// <see cref="MeetingFormatResult"/>: <see cref="Error"/> is set only for an
/// up-front, whole-run failure (e.g. the vault folder is unconfigured) — a
/// per-batch AI extraction failure never sets it, landing instead in
/// <see cref="ExtractionErrors"/> so the rest of the sync (dictionary use,
/// daily-note writing) still completes.
/// </summary>
public sealed record VaultSyncResult(
    int WrittenDays,
    int ExtractedBatches,
    IReadOnlyList<string> ExtractionErrors,
    int TotalEntities,
    int LinkedTargets,
    string? Error = null)
{
    public bool Succeeded => Error is null;

    public static VaultSyncResult Failure(string error) => new(0, 0, [], 0, 0, error);
}

/// <summary>
/// Pure gathering of the free text an Obsidian vault sync would send for AI entity
/// extraction: every non-blank quick-note text plus, for each formatted meeting in
/// range, its session title and latest summary line. No IO — the caller already
/// fetched both lists (see <c>AppServices.SyncVaultAsync</c>). Shared by the sync
/// itself and by the tray confirmation prompt's up-front text count, so the count
/// shown to the user before sending is always exactly what would be sent.
/// </summary>
public static class VaultExtractionTextGatherer
{
    public static IReadOnlyList<string> Gather(
        IReadOnlyList<QuickNote> quickNotes,
        IReadOnlyList<(MeetingSession Session, MeetingSummary Summary)> formattedMeetings)
    {
        ArgumentNullException.ThrowIfNull(quickNotes);
        ArgumentNullException.ThrowIfNull(formattedMeetings);

        var texts = new List<string>();
        texts.AddRange(quickNotes
            .Select(note => note.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text)));

        foreach (var (session, summary) in formattedMeetings)
        {
            if (!string.IsNullOrWhiteSpace(session.Title))
            {
                texts.Add(session.Title);
            }

            if (!string.IsNullOrWhiteSpace(summary.SummaryLine))
            {
                texts.Add(summary.SummaryLine);
            }
        }

        return texts;
    }
}

/// <summary>
/// Pure splitting of a text corpus into batches within
/// <c>WorkLogAI.Infrastructure.EntityExtractionPayloadBuilder</c>'s per-request
/// caps (count and UTF-8 byte budget) — that class and
/// <see cref="IEntityExtractionClient"/> each take exactly one already-sized batch and
/// fail politely if it is still too large; splitting a larger corpus into several
/// sequential requests is this caller-side concern. No IO. A single text whose own
/// UTF-8 size already exceeds the byte budget is never split — it becomes a
/// one-item batch on its own, since a text cannot be partially sent.
/// </summary>
public static class EntityExtractionBatcher
{
    public static IReadOnlyList<IReadOnlyList<string>> Batch(
        IReadOnlyList<string> texts,
        int maxCount,
        int maxUtf8BytesPerBatch)
    {
        ArgumentNullException.ThrowIfNull(texts);
        if (maxCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCount), maxCount, "Must be positive.");
        }
        if (maxUtf8BytesPerBatch <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxUtf8BytesPerBatch), maxUtf8BytesPerBatch, "Must be positive.");
        }

        var batches = new List<IReadOnlyList<string>>();
        var current = new List<string>();
        var currentBytes = 0;

        foreach (var text in texts)
        {
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            var byteCount = Encoding.UTF8.GetByteCount(text);
            var wouldExceedCount = current.Count >= maxCount;
            var wouldExceedBytes = current.Count > 0 && currentBytes + byteCount > maxUtf8BytesPerBatch;
            if (wouldExceedCount || wouldExceedBytes)
            {
                batches.Add(current);
                current = [];
                currentBytes = 0;
            }

            current.Add(text);
            currentBytes += byteCount;
        }

        if (current.Count > 0)
        {
            batches.Add(current);
        }

        return batches;
    }
}

/// <summary>
/// Applies an entity-link transform to a meeting Markdown document's body only,
/// leaving the YAML front matter (which holds the title/participants/tags — never a
/// place for a rewritten <c>[[...]]</c> wikilink) untouched. Pure string splitting;
/// no IO, no entity lookup — the caller supplies the already-resolved transform (see
/// <see cref="EntityLinker.Link"/>). The split point is the end of the front matter's
/// closing <c>---</c> line, matching exactly what
/// <see cref="MeetingMarkdownBuilder"/>'s <c>AppendFrontMatter</c> always emits; text
/// with no leading <c>---</c> front matter (or one with no closing delimiter, which
/// should not occur for output from this codebase) is linked in full as a safe
/// fallback rather than left completely unlinked.
/// </summary>
public static class MeetingMarkdownLinker
{
    private const string Delimiter = "---";

    public static string LinkBody(string? markdown, Func<string, string> linkText)
    {
        ArgumentNullException.ThrowIfNull(linkText);
        if (string.IsNullOrEmpty(markdown))
        {
            return markdown ?? string.Empty;
        }

        if (!markdown.StartsWith(Delimiter, StringComparison.Ordinal))
        {
            return linkText(markdown);
        }

        var closingIndex = markdown.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (closingIndex < 0)
        {
            return linkText(markdown);
        }

        var afterClosingDashes = closingIndex + 4; // skip past "\n---"
        var lineEnd = markdown.IndexOf('\n', afterClosingDashes);
        var splitPoint = lineEnd < 0 ? markdown.Length : lineEnd + 1;

        var frontMatter = markdown[..splitPoint];
        var body = markdown[splitPoint..];
        return frontMatter + linkText(body);
    }
}
