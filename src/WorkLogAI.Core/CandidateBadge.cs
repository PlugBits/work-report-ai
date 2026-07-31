namespace WorkLogAI.Core;

/// <summary>
/// Resolves the small colored origin badge shown on each weekly-review card: AI
/// candidates and manually-added rows get a fixed label/color from
/// <see cref="ReportCandidate.Origin"/>; local-origin rows (built directly from a
/// collected <see cref="SourceEvent"/> by <see cref="LocalSourceEventMapper"/>) are
/// labeled by their backing event's <see cref="SourceTypes"/> instead, since
/// "local" alone does not tell the user what kind of local record it is. Pure and
/// display-only — the App layer maps <see cref="Badge.HexColor"/> to a UI brush.
/// </summary>
public static class CandidateBadge
{
    public readonly record struct Badge(string Label, string HexColor);

    /// <param name="origin">One of the <see cref="CandidateOrigins"/> constants.</param>
    /// <param name="firstSourceType">
    /// The <see cref="SourceTypes"/> of the candidate's first backing source event,
    /// or null/blank/unrecognized when there is none available. Only consulted for
    /// <see cref="CandidateOrigins.Local"/> origin — AI and manual rows always get
    /// their fixed badge regardless of this value.
    /// </param>
    public static Badge Resolve(string origin, string? firstSourceType)
    {
        if (origin == CandidateOrigins.Ai)
        {
            return new Badge("AI", "#2563EB");
        }

        if (origin == CandidateOrigins.Manual)
        {
            return new Badge("手動追加", "#0D9488");
        }

        return firstSourceType switch
        {
            SourceTypes.Manual => new Badge("メモ", "#16A34A"),
            SourceTypes.Meeting => new Badge("議事録", "#7C3AED"),
            SourceTypes.Git => new Badge("Git", "#6B7280"),
            SourceTypes.Codex => new Badge("Codex", "#6B7280"),
            SourceTypes.File => new Badge("ファイル", "#D97706"),
            SourceTypes.OutlookMail => new Badge("メール", "#0284C7"),
            SourceTypes.Calendar => new Badge("予定", "#0284C7"),
            _ => new Badge("ローカル", "#6B7280")
        };
    }
}
