using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using WorkLogAI.Core;

namespace WorkLogAI.Infrastructure;

/// <summary>
/// The would-be payload for an AI entity-extraction request: the sanitized JSON
/// input string plus size bookkeeping. <see cref="EntityExtractionClient"/> uses
/// <see cref="Utf8ByteCount"/> to enforce the 64 KiB hard cap before sending.
/// </summary>
public sealed record EntityExtractionPayload(string Input, int TextCount, long Utf8ByteCount);

/// <summary>
/// Pure construction of the AI entity-extraction request payload from the known
/// dictionary and a caller-supplied batch of source texts. Every text passes
/// through <see cref="SafeTextSanitizer"/>. No IO, no network — fully
/// unit-testable. Splitting a larger corpus into multiple batches within
/// <see cref="MaximumTexts"/>/<see cref="MaximumUtf8Bytes"/> is the caller's job —
/// this builder (and <see cref="EntityExtractionClient"/>) take exactly one batch
/// and fail politely when it is too large.
/// </summary>
public static class EntityExtractionPayloadBuilder
{
    public const int MaximumTexts = 100;
    public const long MaximumUtf8Bytes = 64 * 1024;
    public const int MaximumKnownNames = 200;

    // See MeetingFormatPayloadBuilder for why relaxed escaping is used: the default
    // encoder would roughly double the UTF-8 size of Japanese text.
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static EntityExtractionPayload Build(
        IReadOnlyList<string> knownCanonicalNames,
        IReadOnlyList<string> texts)
    {
        ArgumentNullException.ThrowIfNull(knownCanonicalNames);
        ArgumentNullException.ThrowIfNull(texts);

        var known = knownCanonicalNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Take(MaximumKnownNames)
            .Select(name => SafeTextSanitizer.Sanitize(name, 200))
            .Where(name => name.Length > 0)
            .ToArray();

        var sanitizedTexts = texts
            .Select(text => SafeTextSanitizer.Sanitize(text, 4_000))
            .Where(text => text.Length > 0)
            .ToArray();

        var input = JsonSerializer.Serialize(
            new { known, texts = sanitizedTexts },
            SerializerOptions);

        return new EntityExtractionPayload(input, sanitizedTexts.Length, Encoding.UTF8.GetByteCount(input));
    }
}
