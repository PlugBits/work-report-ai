namespace WorkLogAI.Core;

/// <summary>
/// Transforms a stored git <see cref="SourceEvent"/> body for display/prompt use only.
/// Stored git event bodies (see LocalGitCollector) are never altered — they feed the
/// content hash — so this operates purely on the string at read time.
/// </summary>
public static class GitEventText
{
    private const string FileListMarker = "変更ファイル:";
    private const string StatsSeparator = "。統計:";

    /// <summary>
    /// Removes the "変更ファイル: …" segment (from the marker up to the following
    /// "。統計:" part, or to the end of the string when no 統計 part follows), keeping
    /// the commit-body summary and the 統計 part intact and normalizing any leftover
    /// "。" separators (no leading, trailing, or doubled "。").
    /// </summary>
    public static string StripFileList(string body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return string.Empty;
        }

        var markerIndex = body.IndexOf(FileListMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return body;
        }

        var removalStart = markerIndex > 0 && body[markerIndex - 1] == '。'
            ? markerIndex - 1
            : markerIndex;
        var statsIndex = body.IndexOf(StatsSeparator, markerIndex, StringComparison.Ordinal);
        var removalEnd = statsIndex >= 0 ? statsIndex : body.Length;

        var result = body[..removalStart] + body[removalEnd..];
        return Normalize(result);
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
