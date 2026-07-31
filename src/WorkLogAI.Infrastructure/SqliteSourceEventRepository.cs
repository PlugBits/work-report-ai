using System.Globalization;
using Microsoft.Data.Sqlite;
using WorkLogAI.Core;

namespace WorkLogAI.Infrastructure;

public sealed class SqliteSourceEventRepository(SqliteConnectionFactory connectionFactory)
    : ISourceEventRepository
{
    public async Task<bool> InsertIfNewAsync(
        SourceEvent sourceEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceEvent);
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO source_events
                (id, occurred_at, source_type, title, body, evidence, source_ref,
                 confidence, content_hash, collected_at)
            VALUES
                ($id, $occurredAt, $sourceType, $title, $body, $evidence, $sourceRef,
                 $confidence, $contentHash, $collectedAt);
            """;
        command.Parameters.AddWithValue("$id", sourceEvent.Id.ToString("D"));
        command.Parameters.AddWithValue("$occurredAt", Format(sourceEvent.OccurredAt));
        command.Parameters.AddWithValue("$sourceType", sourceEvent.SourceType);
        command.Parameters.AddWithValue("$title", sourceEvent.Title);
        command.Parameters.AddWithValue("$body", sourceEvent.Body);
        command.Parameters.AddWithValue("$evidence", sourceEvent.Evidence);
        command.Parameters.AddWithValue("$sourceRef", sourceEvent.SourceRef);
        command.Parameters.AddWithValue("$confidence", sourceEvent.Confidence);
        command.Parameters.AddWithValue("$contentHash", sourceEvent.ContentHash);
        command.Parameters.AddWithValue("$collectedAt", Format(sourceEvent.CollectedAt));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<IReadOnlyList<SourceEvent>> ListAsync(
        WeekRange range,
        CancellationToken cancellationToken = default)
    {
        var events = new List<SourceEvent>();
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, occurred_at, source_type, title, body, evidence, source_ref,
                   confidence, content_hash, collected_at
            FROM source_events
            WHERE substr(occurred_at, 1, 10) >= $start
              AND substr(occurred_at, 1, 10) <= $end
            ORDER BY occurred_at, id;
            """;
        command.Parameters.AddWithValue("$start", range.Start.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$end", range.End.ToString("yyyy-MM-dd"));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new SourceEvent(
                Guid.Parse(reader.GetString(0)),
                Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetDouble(7),
                reader.GetString(8),
                Parse(reader.GetString(9))));
        }

        return events;
    }

    public async Task<IReadOnlyList<SourceEvent>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var events = new List<SourceEvent>();
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var parameters = ids.Distinct().Select((id, index) => (id, name: $"$id{index}")).ToArray();
        command.CommandText = $"""
            SELECT id, occurred_at, source_type, title, body, evidence, source_ref,
                   confidence, content_hash, collected_at
            FROM source_events
            WHERE id IN ({string.Join(",", parameters.Select(item => item.name))})
            ORDER BY occurred_at, id;
            """;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.name, parameter.id.ToString("D"));
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new SourceEvent(
                Guid.Parse(reader.GetString(0)),
                Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetDouble(7),
                reader.GetString(8),
                Parse(reader.GetString(9))));
        }
        return events;
    }

    public async Task<int> DeleteByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        var distinctIds = ids.Distinct().ToArray();
        if (distinctIds.Length == 0)
        {
            return 0;
        }

        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        var deleted = 0;
        const int chunkSize = 500;
        foreach (var chunk in distinctIds.Chunk(chunkSize))
        {
            await using var command = connection.CreateCommand();
            var parameters = chunk.Select((id, index) => (id, name: $"$id{index}")).ToArray();
            command.CommandText = $"""
                DELETE FROM source_events
                WHERE id IN ({string.Join(",", parameters.Select(item => item.name))});
                """;
            foreach (var parameter in parameters)
            {
                command.Parameters.AddWithValue(parameter.name, parameter.id.ToString("D"));
            }
            deleted += await command.ExecuteNonQueryAsync(cancellationToken);
        }
        return deleted;
    }

    public async Task<IReadOnlyList<Guid>> ListIdsOlderThanAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default)
    {
        var ids = new List<Guid>();
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id
            FROM source_events
            WHERE julianday(occurred_at) < julianday($cutoff);
            """;
        command.Parameters.AddWithValue("$cutoff", Format(cutoff));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(Guid.Parse(reader.GetString(0)));
        }

        return ids;
    }

    private static string Format(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
