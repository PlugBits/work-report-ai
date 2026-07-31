using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using WorkLogAI.Core;

namespace WorkLogAI.Infrastructure;

/// <summary>
/// The would-be payload for an AI meeting-formatting request: the sanitized JSON
/// input string plus size/line bookkeeping. MeetingSendPreviewWindow recomputes
/// this on every checkbox toggle to show the header; MeetingFormatClient uses
/// <see cref="Utf8ByteCount"/> to enforce the 256 KiB hard cap before sending.
/// </summary>
public sealed record MeetingFormatPayload(
    string Input,
    int LineCount,
    long Utf8ByteCount);

/// <summary>
/// Pure construction of the AI meeting-formatting request payload from a session
/// and a caller-selected subset of lines. Lines the caller omits (unchecked in the
/// send preview) are simply absent from the result — SQLite is never touched here.
/// Every text field (title, participants, line text) passes through
/// <see cref="SafeTextSanitizer"/> and local filesystem paths are further redacted
/// before serialization; the session id and any other local identifiers never
/// appear in the payload. No IO, no network — fully unit-testable.
/// </summary>
public static partial class MeetingFormatPayloadBuilder
{
    public const int MaximumUtf8Bytes = 256 * 1024;

    // The default encoder escapes most non-ASCII characters as \uXXXX, which would
    // roughly double the UTF-8 size of Japanese text and make the 256 KiB cap
    // measure an inflated, misleading number. Relaxed escaping keeps ordinary
    // Japanese text as-is while still escaping the handful of characters JSON
    // requires (quotes, backslash, control characters).
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static MeetingFormatPayload Build(MeetingSession session, IReadOnlyList<MeetingLine> includedLines)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(includedLines);

        var title = Redact(SafeTextSanitizer.Sanitize(session.Title, 300));
        var participants = Redact(SafeTextSanitizer.Sanitize(session.Participants, 300));
        var lines = includedLines
            .OrderBy(line => line.LineNo)
            .Select(FormatLine)
            .ToArray();

        var input = JsonSerializer.Serialize(
            new
            {
                title,
                kind = MeetingKindStrings.ToStorageString(session.Kind),
                participants,
                startedAt = session.StartedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                lines
            },
            SerializerOptions);

        return new MeetingFormatPayload(input, lines.Length, Encoding.UTF8.GetByteCount(input));
    }

    private static string FormatLine(MeetingLine line)
    {
        var text = Redact(SafeTextSanitizer.Sanitize(line.Text, 500));
        return MeetingLineFormatter.Compose(line.LoggedAt, line.Marker, text);
    }

    private static string Redact(string value)
    {
        var reduced = WindowsPath().Replace(value, "[LOCAL_PATH]");
        return UnixPath().Replace(reduced, "[LOCAL_PATH]");
    }

    [GeneratedRegex(@"(?i)(?:[A-Z]:\\|\\\\)[^\s;,]+", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsPath();

    [GeneratedRegex(@"(?<!\w)/(?:Users|home|var|tmp|opt)/[^\s;,]+", RegexOptions.CultureInvariant)]
    private static partial Regex UnixPath();
}
