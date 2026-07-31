using WorkLogAI.Core;

namespace WorkLogAI.Infrastructure;

/// <summary>
/// Formats the review card's 根拠 (evidence) line for a <see cref="SourceEvent"/>.
/// Display only — never feeds a content hash, so it is safe to change the wording
/// here without duplicating already-collected events (unlike the collector-produced
/// Title/Evidence/Body strings themselves).
/// </summary>
public static class EvidenceFormatter
{
    private const int SnippetLimit = 200;
    private const int TitleLimit = 300;
    private const int EvidenceLimit = 800;

    /// <summary>
    /// For 手動メモ (manual) and 議事録 (meeting) events, shows the actual memo text /
    /// summary line (<see cref="SourceEvent.Body"/>) so the user can tell which memo
    /// backs the row. Every other source keeps the existing Title — Evidence format.
    /// </summary>
    public static string Describe(SourceEvent source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var label = JapaneseSourceLabel(source.SourceType);
        var snippet = source.SourceType is SourceTypes.Manual or SourceTypes.Meeting
            ? SafeTextSanitizer.Sanitize(source.Body, SnippetLimit)
            : $"{SafeTextSanitizer.Sanitize(source.Title, TitleLimit)} — " +
              $"{SafeTextSanitizer.Sanitize(source.Evidence, EvidenceLimit)}";
        return $"{source.OccurredAt:yyyy/MM/dd HH:mm} [{label}] {snippet}";
    }

    public static string Describe(IReadOnlyList<Guid> ids, IReadOnlyDictionary<Guid, SourceEvent> events) =>
        string.Join(
            "\n",
            ids.Select(id => events.TryGetValue(id, out var source)
                ? Describe(source)
                : $"[{id:D}] 根拠詳細なし"));

    private static string JapaneseSourceLabel(string sourceType) => sourceType switch
    {
        SourceTypes.Manual => "手動メモ",
        SourceTypes.Git => "Git",
        SourceTypes.Codex => "Codex",
        SourceTypes.File => "更新ファイル",
        SourceTypes.OutlookMail => "メール",
        SourceTypes.Calendar => "予定",
        SourceTypes.Meeting => "議事録",
        _ => sourceType
    };
}
