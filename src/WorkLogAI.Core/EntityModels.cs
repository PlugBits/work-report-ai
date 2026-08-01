namespace WorkLogAI.Core;

/// <summary>
/// A single row of the self-growing entity dictionary: a canonical name (customer,
/// project, part number, person, or system) plus every alias it has accumulated,
/// how many times it has been observed, and whether the user has excluded it from
/// daily-note linking. Persisted by <see cref="IEntityRepository"/>.
/// </summary>
public sealed record WorkEntity(
    Guid Id,
    string CanonicalName,
    string Kind,
    int OccurrenceCount,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    bool Excluded,
    IReadOnlyList<string> Aliases);

/// <summary>
/// One AI-extracted (or otherwise observed) mention of an entity, ready to be
/// merged into the dictionary by <see cref="IEntityRepository.UpsertObservationsAsync"/>.
/// <see cref="Aliases"/> excludes <see cref="CanonicalName"/> itself.
/// </summary>
public sealed record EntityObservation(
    string CanonicalName,
    IReadOnlyList<string> Aliases,
    string Kind,
    int Occurrences);

/// <summary>
/// The loose, open-ended set of entity kinds recognized by the vault feature.
/// "other" is both the default and the safe fallback for anything unrecognized —
/// never a validation failure.
/// </summary>
public static class EntityKinds
{
    public const string Customer = "customer";
    public const string Project = "project";
    public const string Part = "part";
    public const string Person = "person";
    public const string System = "system";
    public const string Other = "other";

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        Customer, Project, Part, Person, System, Other
    };

    /// <summary>Returns <paramref name="value"/> unchanged if it is one of the known
    /// kinds, otherwise <see cref="Other"/>.</summary>
    public static string Normalize(string? value) =>
        value is not null && Known.Contains(value) ? value : Other;
}

/// <summary>
/// A dictionary entry ready to be matched against free text by
/// <see cref="EntityLinker"/>: a canonical display name plus every alias that
/// should also resolve to it.
/// </summary>
public sealed record EntityLinkTarget(string Canonical, IReadOnlyList<string> Aliases);

/// <summary>
/// Selects which dictionary entities are worth auto-linking in daily notes. Kept
/// separate from <see cref="EntityLinker"/> itself so the linker stays a pure
/// string-rewriting function with no opinion on thresholds.
/// </summary>
public static class EntityLinkTargets
{
    /// <summary>
    /// Excludes entities the user has marked <see cref="WorkEntity.Excluded"/> and
    /// any whose <see cref="WorkEntity.OccurrenceCount"/> is below
    /// <paramref name="minOccurrences"/> — a fresh, once-seen name is noise, not a
    /// worthwhile link target yet.
    /// </summary>
    public static IReadOnlyList<EntityLinkTarget> From(
        IReadOnlyList<WorkEntity> entities,
        int minOccurrences)
    {
        ArgumentNullException.ThrowIfNull(entities);
        return entities
            .Where(entity => !entity.Excluded && entity.OccurrenceCount >= minOccurrences)
            .Select(entity => new EntityLinkTarget(entity.CanonicalName, entity.Aliases))
            .ToArray();
    }
}
