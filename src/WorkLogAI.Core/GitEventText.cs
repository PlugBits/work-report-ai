using System.Text;

namespace WorkLogAI.Core;

/// <summary>
/// Transforms a stored git <see cref="SourceEvent"/> body (or an already-merged
/// candidate activity built from several such bodies — see CandidateMergeService)
/// for display/prompt use only. Stored git event bodies (see LocalGitCollector) are
/// never altered — they feed the content hash — so this operates purely on the
/// string at read time.
/// </summary>
public static class GitEventText
{
    private const string FileListMarker = "変更ファイル:";
    private const string StatsSeparator = "。統計:";

    // Merged candidate activities join distinct chunks with " — " (em dash) between
    // a source's title and body (see LocalSourceEventMapper.JoinNonBlank) and " / "
    // between merged activities (see CandidateMergeService.JoinDistinct). A file list
    // is always a comma-joined list of paths, so it never itself contains " / " —
    // any " / " encountered while scanning inside a "変更ファイル:" segment can only
    // be the start of the next merged chunk, never part of the file list.
    private const string ChunkSeparator = " / ";
    private const string TitleSeparator = " — ";

    /// <summary>
    /// Removes every "変更ファイル: …" segment from <paramref name="body"/>, one per
    /// occurrence. Each segment runs from its "変更ファイル:" marker up to the
    /// earliest of: the following "。統計:" part (its "。" is absorbed so the kept
    /// text reads "…統計: …" with no dangling "。"), a " / " that starts the next
    /// merged chunk (kept, along with everything after it), or the end of the
    /// string. A commit-body summary and the 統計 part are always kept intact.
    /// Leftover "。" and " — " separators that a segment's removal would otherwise
    /// strand (e.g. a chunk reduced to "subject — " with nothing left after it) are
    /// normalized away, along with any doubled or leading/trailing "。".
    /// </summary>
    public static string StripFileList(string body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return string.Empty;
        }

        if (!body.Contains(FileListMarker, StringComparison.Ordinal))
        {
            return body;
        }

        var result = new StringBuilder(body.Length);
        var position = 0;
        while (true)
        {
            var markerIndex = body.IndexOf(FileListMarker, position, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                result.Append(body, position, body.Length - position);
                break;
            }

            result.Append(body, position, markerIndex - position);

            var statsIndex = body.IndexOf(StatsSeparator, markerIndex, StringComparison.Ordinal);
            var chunkSeparatorIndex = body.IndexOf(ChunkSeparator, markerIndex, StringComparison.Ordinal);

            int removalEnd;
            if (statsIndex >= 0 && (chunkSeparatorIndex < 0 || statsIndex <= chunkSeparatorIndex))
            {
                // Absorb the "。" that precedes "統計:" — it only ever separated the
                // file list from the stats, so once the file list is gone it has
                // nothing left to separate.
                removalEnd = statsIndex + 1;
            }
            else if (chunkSeparatorIndex >= 0)
            {
                // No stats for this chunk: stop right before the next chunk's
                // separator and keep it (and everything after it) untouched.
                removalEnd = chunkSeparatorIndex;
                TrimDanglingTitleSeparator(result);
            }
            else
            {
                // No stats and no further chunk: the file list was the last thing
                // in the string.
                removalEnd = body.Length;
                TrimDanglingTitleSeparator(result);
            }

            position = removalEnd;
        }

        return Normalize(result.ToString());
    }

    /// <summary>
    /// Strips a title/body separator (" — ") left with nothing after it in the
    /// current chunk once its file list has been removed, e.g. "件名 — " becomes
    /// "件名".
    /// </summary>
    private static void TrimDanglingTitleSeparator(StringBuilder builder)
    {
        var trimmedLength = builder.Length;
        while (trimmedLength > 0 && char.IsWhiteSpace(builder[trimmedLength - 1]))
        {
            trimmedLength--;
        }

        var dash = TitleSeparator.Trim();
        if (trimmedLength >= dash.Length
            && builder.ToString(trimmedLength - dash.Length, dash.Length) == dash)
        {
            trimmedLength -= dash.Length;
            while (trimmedLength > 0 && char.IsWhiteSpace(builder[trimmedLength - 1]))
            {
                trimmedLength--;
            }
        }
        else
        {
            return;
        }

        builder.Length = trimmedLength;
    }

    private static string Normalize(string value)
    {
        while (value.Contains("。。", StringComparison.Ordinal))
        {
            value = value.Replace("。。", "。");
        }

        return value.Trim('。').Trim();
    }
}
