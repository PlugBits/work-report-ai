using System.Globalization;
using Microsoft.Data.Sqlite;
using WorkLogAI.Core;

namespace WorkLogAI.Infrastructure;

/// <summary>
/// SQLite-backed <see cref="IEntityRepository"/>. Relies on the
/// <c>COLLATE NOCASE</c> declared on <c>entities.canonical_name</c> and
/// <c>entity_aliases.alias</c> (see migration 006) for every case-insensitive
/// comparison — no <c>ToLower</c> normalization is needed in this class.
/// </summary>
public sealed class SqliteEntityRepository(SqliteConnectionFactory connectionFactory)
    : IEntityRepository
{
    public async Task UpsertObservationsAsync(
        IReadOnlyList<EntityObservation> observations,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (observations.Count == 0)
        {
            return;
        }

        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var sqliteTransaction = (SqliteTransaction)transaction;

        foreach (var observation in observations)
        {
            await UpsertOneAsync(connection, sqliteTransaction, observation, observedAt, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkEntity>> ListAsync(
        bool includeExcluded = false,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var entities = new List<(Guid Id, string Canonical, string Kind, int Count, DateTimeOffset First, DateTimeOffset Last, bool Excluded)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = includeExcluded
                ? """
                    SELECT id, canonical_name, kind, occurrence_count, first_seen_at, last_seen_at, excluded
                    FROM entities
                    ORDER BY canonical_name;
                    """
                : """
                    SELECT id, canonical_name, kind, occurrence_count, first_seen_at, last_seen_at, excluded
                    FROM entities
                    WHERE excluded = 0
                    ORDER BY canonical_name;
                    """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                entities.Add((
                    Guid.Parse(reader.GetString(0)),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
                    DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
                    reader.GetInt32(6) != 0));
            }
        }

        var aliasesByEntity = new Dictionary<Guid, List<string>>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT entity_id, alias FROM entity_aliases ORDER BY alias;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var entityId = Guid.Parse(reader.GetString(0));
                if (!aliasesByEntity.TryGetValue(entityId, out var list))
                {
                    list = [];
                    aliasesByEntity[entityId] = list;
                }
                list.Add(reader.GetString(1));
            }
        }

        return entities
            .Select(entity => new WorkEntity(
                entity.Id,
                entity.Canonical,
                entity.Kind,
                entity.Count,
                entity.First,
                entity.Last,
                entity.Excluded,
                aliasesByEntity.TryGetValue(entity.Id, out var aliases) ? aliases : []))
            .ToArray();
    }

    public async Task ReplaceExclusionsAsync(
        IReadOnlyCollection<string> canonicalNames,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(canonicalNames);

        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var sqliteTransaction = (SqliteTransaction)transaction;

        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = sqliteTransaction;
            clear.CommandText = "UPDATE entities SET excluded = 0;";
            await clear.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var name in canonicalNames)
        {
            var trimmed = name?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            await using var set = connection.CreateCommand();
            set.Transaction = sqliteTransaction;
            set.CommandText = "UPDATE entities SET excluded = 1 WHERE canonical_name = $name;";
            set.Parameters.AddWithValue("$name", trimmed);
            await set.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task UpsertOneAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        EntityObservation observation,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var canonical = observation.CanonicalName?.Trim();
        if (string.IsNullOrEmpty(canonical))
        {
            return;
        }

        var kind = EntityKinds.Normalize(observation.Kind);
        var occurrences = Math.Max(observation.Occurrences, 1);
        var aliasCandidates = (observation.Aliases ?? [])
            .Select(alias => alias?.Trim() ?? string.Empty)
            .Where(alias => alias.Length > 0)
            .ToArray();

        // Matching is deliberately keyed on the observation's own canonical name
        // only (against existing canonical names OR aliases) — not on the
        // observation's own aliases. Otherwise two distinct, newly-seen entities
        // that happen to share one proposed alias would incorrectly collapse into
        // a single entity instead of each keeping their own identity and letting
        // the alias go to whichever one claims it first (see MergeAliasesAsync).
        var match = await FindEntityAsync(connection, transaction, canonical, cancellationToken);

        if (match is null)
        {
            var id = Guid.NewGuid();
            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO entities
                        (id, canonical_name, kind, occurrence_count, first_seen_at, last_seen_at, excluded, created_at)
                    VALUES
                        ($id, $canonical, $kind, $occurrences, $observedAt, $observedAt, 0, $observedAt);
                    """;
                insert.Parameters.AddWithValue("$id", id.ToString("D"));
                insert.Parameters.AddWithValue("$canonical", canonical);
                insert.Parameters.AddWithValue("$kind", kind);
                insert.Parameters.AddWithValue("$occurrences", occurrences);
                insert.Parameters.AddWithValue("$observedAt", FormatTimestamp(observedAt));
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await MergeAliasesAsync(connection, transaction, id, canonical, aliasCandidates, cancellationToken);
            return;
        }

        var (matchedId, matchedCanonical) = match.Value;
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE entities
                SET occurrence_count = occurrence_count + $occurrences,
                    last_seen_at = $observedAt,
                    kind = CASE WHEN kind = 'other' AND $kind <> 'other' THEN $kind ELSE kind END
                WHERE id = $id;
                """;
            update.Parameters.AddWithValue("$occurrences", occurrences);
            update.Parameters.AddWithValue("$observedAt", FormatTimestamp(observedAt));
            update.Parameters.AddWithValue("$kind", kind);
            update.Parameters.AddWithValue("$id", matchedId.ToString("D"));
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        // The observation's own canonical name is a legitimate alias of the matched
        // entity whenever it differs from the entity's actual canonical name (i.e.
        // the match was found via an alias, not the canonical column itself).
        var mergeCandidates = aliasCandidates;
        if (!string.Equals(canonical, matchedCanonical, StringComparison.OrdinalIgnoreCase))
        {
            mergeCandidates = [canonical, .. aliasCandidates];
        }

        await MergeAliasesAsync(connection, transaction, matchedId, matchedCanonical, mergeCandidates, cancellationToken);
    }

    private static async Task<(Guid Id, string Canonical)?> FindEntityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string name,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, canonical_name FROM entities WHERE canonical_name = $name
            UNION
            SELECT e.id, e.canonical_name
            FROM entities e
            JOIN entity_aliases a ON a.entity_id = e.id
            WHERE a.alias = $name
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$name", name);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return (Guid.Parse(reader.GetString(0)), reader.GetString(1));
    }

    private static async Task MergeAliasesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid entityId,
        string entityCanonical,
        IReadOnlyList<string> aliasCandidates,
        CancellationToken cancellationToken)
    {
        foreach (var alias in aliasCandidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (alias.Equals(entityCanonical, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var owner = await FindEntityAsync(connection, transaction, alias, cancellationToken);
            if (owner is not null)
            {
                // Either already this entity's alias (no-op) or another entity's
                // alias/canonical name (collision — first owner wins, skip).
                continue;
            }

            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO entity_aliases (id, entity_id, alias)
                VALUES ($id, $entityId, $alias);
                """;
            insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
            insert.Parameters.AddWithValue("$entityId", entityId.ToString("D"));
            insert.Parameters.AddWithValue("$alias", alias);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);
}
