using System.Text.Json.Serialization;

namespace WorkLogAI.Core;

/// <summary>
/// A single batch of source text to run entity extraction over, plus the current
/// dictionary (canonical names only) so the model can prefer known spellings.
/// Splitting a larger corpus into batches within the size limits enforced by
/// <see cref="IEntityExtractionClient"/> is the caller's responsibility.
/// </summary>
public sealed record EntityExtractionRequest(
    IReadOnlyList<string> KnownCanonicalNames,
    IReadOnlyList<string> Texts,
    string Model);

/// <summary>
/// Outcome of an AI entity-extraction attempt. Mirrors <see cref="MeetingFormatResult"/>'s
/// Succeeded/Error convention. <see cref="Error"/> is always a safe, generic Japanese
/// message — it must never echo request or response bodies.
/// </summary>
public sealed record EntityExtractionResult(
    IReadOnlyList<EntityObservation>? Observations,
    string? Error = null)
{
    public bool Succeeded => Error is null && Observations is not null;
}

public interface IEntityExtractionClient
{
    Task<EntityExtractionResult> ExtractAsync(
        EntityExtractionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Raw structured-output shape requested from the model. Validated and
/// converted to <see cref="EntityObservation"/>s by <see cref="EntityExtractionValidator"/>
/// before it is trusted anywhere else.</summary>
public sealed class EntityExtractionEnvelope
{
    [JsonPropertyName("entities")]
    public required List<EntityExtractionItemPayload> Entities { get; init; }
}

public sealed class EntityExtractionItemPayload
{
    [JsonPropertyName("canonical")]
    public required string Canonical { get; init; }

    [JsonPropertyName("aliases")]
    public required List<string> Aliases { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("occurrences")]
    public required int Occurrences { get; init; }
}

/// <summary>
/// Independent, pure validation of a model's structured entity-extraction output —
/// no IO, no network. Unlike <see cref="MeetingFormatValidator"/> (where one bad
/// item invalidates the whole response), a single malformed entity is simply
/// dropped: losing one candidate out of a batch is harmless, while discarding an
/// entire extraction over one bad item would defeat the purpose of a
/// self-growing, best-effort dictionary. Malformed here means a blank or overlong
/// canonical name; an unrecognized <c>kind</c> falls back to
/// <see cref="EntityKinds.Other"/> and a non-positive <c>occurrences</c> is
/// coerced up to 1, per <see cref="EntityKinds.Normalize"/>.
/// </summary>
public static class EntityExtractionValidator
{
    public const int MaximumCanonicalLength = 100;

    public static EntityExtractionResult Validate(EntityExtractionEnvelope? envelope)
    {
        if (envelope?.Entities is null)
        {
            return new EntityExtractionResult(null, "AIの応答が空でした。");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<EntityObservation>();
        foreach (var item in envelope.Entities)
        {
            var canonical = item.Canonical?.Trim() ?? string.Empty;
            if (canonical.Length == 0 || canonical.Length > MaximumCanonicalLength)
            {
                continue;
            }

            if (!seen.Add(canonical))
            {
                continue;
            }

            var occurrences = item.Occurrences < 1 ? 1 : item.Occurrences;
            var kind = EntityKinds.Normalize(item.Kind);
            var aliases = (item.Aliases ?? [])
                .Select(alias => alias?.Trim() ?? string.Empty)
                .Where(alias => alias.Length > 0 && !alias.Equals(canonical, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            results.Add(new EntityObservation(canonical, aliases, kind, occurrences));
        }

        return new EntityExtractionResult(results, null);
    }
}
