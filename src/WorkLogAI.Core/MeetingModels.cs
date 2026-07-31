using System.Globalization;
using System.Text.Json;

namespace WorkLogAI.Core;

public enum MeetingKind
{
    Meeting,
    Visitor,
    Call
}

public enum MeetingStatus
{
    Draft,
    Closed,
    Formatted
}

public enum MeetingMarker
{
    None,
    Todo,
    Decision
}

public static class MeetingKindStrings
{
    public const string Meeting = "meeting";
    public const string Visitor = "visitor";
    public const string Call = "call";

    public static string ToStorageString(MeetingKind kind) => kind switch
    {
        MeetingKind.Meeting => Meeting,
        MeetingKind.Visitor => Visitor,
        MeetingKind.Call => Call,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown meeting kind.")
    };

    public static MeetingKind Parse(string value) => value switch
    {
        Meeting => MeetingKind.Meeting,
        Visitor => MeetingKind.Visitor,
        Call => MeetingKind.Call,
        _ => throw new ArgumentException($"Unknown meeting kind '{value}'.", nameof(value))
    };
}

public static class MeetingStatusStrings
{
    public const string Draft = "draft";
    public const string Closed = "closed";
    public const string Formatted = "formatted";

    public static string ToStorageString(MeetingStatus status) => status switch
    {
        MeetingStatus.Draft => Draft,
        MeetingStatus.Closed => Closed,
        MeetingStatus.Formatted => Formatted,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown meeting status.")
    };

    public static MeetingStatus Parse(string value) => value switch
    {
        Draft => MeetingStatus.Draft,
        Closed => MeetingStatus.Closed,
        Formatted => MeetingStatus.Formatted,
        _ => throw new ArgumentException($"Unknown meeting status '{value}'.", nameof(value))
    };
}

public static class MeetingMarkerStrings
{
    public const string None = "none";
    public const string Todo = "todo";
    public const string Decision = "decision";

    public static string ToStorageString(MeetingMarker marker) => marker switch
    {
        MeetingMarker.None => None,
        MeetingMarker.Todo => Todo,
        MeetingMarker.Decision => Decision,
        _ => throw new ArgumentOutOfRangeException(nameof(marker), marker, "Unknown meeting marker.")
    };

    public static MeetingMarker Parse(string value) => value switch
    {
        None => MeetingMarker.None,
        Todo => MeetingMarker.Todo,
        Decision => MeetingMarker.Decision,
        _ => throw new ArgumentException($"Unknown meeting marker '{value}'.", nameof(value))
    };
}

public sealed record MeetingSession(
    Guid Id,
    string Title,
    string Participants,
    MeetingKind Kind,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    MeetingStatus Status,
    DateTimeOffset CreatedAt);

public sealed record MeetingLine(
    Guid Id,
    Guid SessionId,
    int LineNo,
    MeetingMarker Marker,
    string Text,
    DateTimeOffset LoggedAt);

/// <summary>One AI-formatting run persisted for a session. A session can accumulate
/// several rows over time (re-formatting after edits); callers that need "the"
/// summary use <see cref="IMeetingRepository.GetLatestSummaryAsync"/>.</summary>
public sealed record MeetingSummary(
    Guid Id,
    Guid SessionId,
    string FormattedJson,
    string SummaryLine,
    DateTimeOffset CreatedAt);

/// <summary>
/// Shared "HH:mm [宿題|決定|] text" rendering used by the raw log section, the
/// meeting capture list, and the mandatory AI-send preview so all three agree on
/// what a line looks like.
/// </summary>
public static class MeetingLineFormatter
{
    public static string BadgeLabel(MeetingMarker marker) => marker switch
    {
        MeetingMarker.Todo => "[宿題]",
        MeetingMarker.Decision => "[決定]",
        _ => string.Empty
    };

    public static string Compose(DateTimeOffset loggedAt, MeetingMarker marker, string text)
    {
        var time = loggedAt.ToString("HH:mm", CultureInfo.InvariantCulture);
        var badge = BadgeLabel(marker);
        return badge.Length == 0 ? $"{time} {text}" : $"{time} {badge} {text}";
    }

    public static string FormatWithTime(MeetingLine line) => Compose(line.LoggedAt, line.Marker, line.Text);
}

/// <summary>
/// The single, explicit round-trip contract for persisting/reloading a
/// <see cref="MeetingFormattedResult"/> as the meeting_summaries.formatted_json
/// column. camelCase, case-insensitive on read.
/// </summary>
public static class MeetingFormattedResultJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(MeetingFormattedResult result) =>
        JsonSerializer.Serialize(result, Options);

    public static MeetingFormattedResult? Deserialize(string json) =>
        JsonSerializer.Deserialize<MeetingFormattedResult>(json, Options);
}

/// <summary>
/// Pure marker parsing for a single raw line of meeting input. No IO, no repository
/// access — the caller decides how to persist the result.
/// </summary>
public static class MeetingLineParser
{
    /// <summary>
    /// Parses a raw line of user input into a marker + trimmed text pair. A leading
    /// '@' (or full-width '＠') marks a todo/action item; a leading '!' (or full-width
    /// '！') marks a decision; anything else is an unmarked note. Returns null when
    /// there is no meaningful text left after stripping whitespace and the marker
    /// prefix (e.g. blank input, or a bare '@'/'!') so the caller can reject it.
    /// </summary>
    public static (MeetingMarker Marker, string Text)? Parse(string? rawInput)
    {
        if (rawInput is null)
        {
            return null;
        }

        var trimmed = rawInput.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        var marker = trimmed[0] switch
        {
            '@' or '＠' => MeetingMarker.Todo,
            '!' or '！' => MeetingMarker.Decision,
            _ => MeetingMarker.None
        };

        var text = marker == MeetingMarker.None ? trimmed : trimmed[1..].Trim();
        return text.Length == 0 ? null : (marker, text);
    }
}

/// <summary>
/// Splits a free-text participants field (e.g. "田中、鈴木, 佐藤") into individual
/// names and formats them back as an inline YAML list. Pure, no IO.
/// </summary>
public static class MeetingParticipantsFormatter
{
    private static readonly char[] Separators = ['、', ',', '，', ' ', '\t', '　'];

    public static IReadOnlyList<string> Split(string? participants)
    {
        if (string.IsNullOrWhiteSpace(participants))
        {
            return [];
        }

        return participants
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    public static string ToYamlInlineList(IReadOnlyList<string> items) =>
        "[" + string.Join(", ", items.Select(EscapeYamlScalar)) + "]";

    private static string EscapeYamlScalar(string value) =>
        value.IndexOfAny([':', '#', '[', ']', '{', '}', ',', '\'', '"']) >= 0
            ? "\"" + value.Replace("\"", "\\\"") + "\""
            : value;
}

/// <summary>
/// Placeholder for the AI-formatted view of a meeting. Null until a later phase
/// populates it from model output; <see cref="MeetingMarkdownBuilder"/> renders the
/// raw log only while this is null.
/// </summary>
public sealed record MeetingFormattedResult(
    string SummaryLine,
    string Overview,
    IReadOnlyList<string> Decisions,
    IReadOnlyList<MeetingActionItem> ActionItems,
    IReadOnlyList<MeetingTopic> Topics);

public sealed record MeetingActionItem(string Text, string? Owner = null, string? Due = null);

public sealed record MeetingTopic(string Title, string Detail);
