using System.Text.Json;
using Microsoft.Data.Sqlite;
using WorkLogAI.Core;

namespace WorkLogAI.Infrastructure;

public sealed class SqliteReportCandidateRepository(SqliteConnectionFactory connectionFactory)
    : IReportCandidateRepository
{
    public async Task ReplaceWeekAsync(
        DateOnly weekStart,
        IReadOnlyCollection<ReportCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = (SqliteTransaction)transaction;
            delete.CommandText = "DELETE FROM report_candidates WHERE week_start = $weekStart;";
            delete.Parameters.AddWithValue("$weekStart", weekStart.ToString("yyyy-MM-dd"));
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var candidate in candidates)
        {
            await InsertAsync(connection, (SqliteTransaction)transaction, candidate, false, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReportCandidate>> ListAsync(
        DateOnly weekStart,
        CancellationToken cancellationToken = default)
    {
        var candidates = new List<ReportCandidate>();
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, week_start, work_date, work_item, activity, result_or_next,
                   status, confidence, selected, edited, source_event_ids_json,
                   needs_confirmation, confirmation_question, origin, category
            FROM report_candidates
            WHERE week_start = $weekStart
            ORDER BY work_date, id;
            """;
        command.Parameters.AddWithValue("$weekStart", weekStart.ToString("yyyy-MM-dd"));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var sourceIds = JsonSerializer.Deserialize<Guid[]>(reader.GetString(10)) ?? [];
            candidates.Add(new ReportCandidate(
                Guid.Parse(reader.GetString(0)),
                DateOnly.Parse(reader.GetString(1)),
                DateOnly.Parse(reader.GetString(2)),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetDouble(7),
                reader.GetInt64(8) == 1,
                reader.GetInt64(9) == 1,
                sourceIds,
                reader.GetInt64(11) == 1,
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.GetString(13),
                reader.GetString(14)));
        }

        return candidates;
    }

    public async Task SaveGeneratedAsync(
        DateOnly weekStart,
        IReadOnlyCollection<ReportCandidate> generatedCandidates,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = (SqliteTransaction)transaction;
            delete.CommandText = """
                DELETE FROM report_candidates
                WHERE week_start = $weekStart
                  AND origin = 'ai'
                  AND edited = 0;
                """;
            delete.Parameters.AddWithValue("$weekStart", weekStart.ToString("yyyy-MM-dd"));
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var candidate in generatedCandidates)
        {
            await InsertAsync(connection, (SqliteTransaction)transaction, candidate, true, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveLocalAsync(
        DateOnly weekStart,
        IReadOnlyCollection<ReportCandidate> localCandidates,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = (SqliteTransaction)transaction;
            delete.CommandText = """
                DELETE FROM report_candidates
                WHERE week_start = $weekStart
                  AND origin = 'local'
                  AND edited = 0;
                """;
            delete.Parameters.AddWithValue("$weekStart", weekStart.ToString("yyyy-MM-dd"));
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var candidate in localCandidates)
        {
            await InsertAsync(connection, (SqliteTransaction)transaction, candidate, true, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public Task SaveReviewAsync(
        DateOnly weekStart,
        IReadOnlyCollection<ReportCandidate> candidates,
        CancellationToken cancellationToken = default) =>
        ReplaceWeekAsync(weekStart, candidates, cancellationToken);

    public async Task<int> SetSelectedAsync(
        IReadOnlyCollection<Guid> ids,
        bool selected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var distinctIds = ids.Distinct().ToArray();
        if (distinctIds.Length == 0)
        {
            return 0;
        }

        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        var updated = 0;
        const int chunkSize = 500;
        foreach (var chunk in distinctIds.Chunk(chunkSize))
        {
            await using var command = connection.CreateCommand();
            var parameters = chunk.Select((id, index) => (id, name: $"$id{index}")).ToArray();
            command.CommandText = $"""
                UPDATE report_candidates
                SET selected = $selected
                WHERE id IN ({string.Join(",", parameters.Select(item => item.name))});
                """;
            command.Parameters.AddWithValue("$selected", selected ? 1 : 0);
            foreach (var parameter in parameters)
            {
                command.Parameters.AddWithValue(parameter.name, parameter.id.ToString("D"));
            }
            updated += await command.ExecuteNonQueryAsync(cancellationToken);
        }
        return updated;
    }

    public async Task<IReadOnlyList<(Guid Id, string Activity)>> ListActivitiesContainingAsync(
        string needle,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(needle);
        var results = new List<(Guid Id, string Activity)>();
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, activity
            FROM report_candidates
            WHERE activity LIKE $needle ESCAPE '\';
            """;
        command.Parameters.AddWithValue("$needle", "%" + EscapeLike(needle) + "%");

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add((Guid.Parse(reader.GetString(0)), reader.GetString(1)));
        }

        return results;
    }

    public async Task UpdateActivityAsync(
        Guid id,
        string activity,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE report_candidates
            SET activity = $activity
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$activity", activity);
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListAllSourceEventIdJsonAsync(
        CancellationToken cancellationToken = default)
    {
        var values = new List<string>();
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT source_event_ids_json FROM report_candidates;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    public async Task<IReadOnlyList<ReportCandidate>> ListSelectedByDateRangeAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken cancellationToken = default)
    {
        var candidates = new List<ReportCandidate>();
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, week_start, work_date, work_item, activity, result_or_next,
                   status, confidence, selected, edited, source_event_ids_json,
                   needs_confirmation, confirmation_question, origin, category
            FROM report_candidates
            WHERE selected = 1
              AND work_date >= $from
              AND work_date <= $to
            ORDER BY work_date, id;
            """;
        command.Parameters.AddWithValue("$from", fromInclusive.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$to", toInclusive.ToString("yyyy-MM-dd"));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var sourceIds = JsonSerializer.Deserialize<Guid[]>(reader.GetString(10)) ?? [];
            candidates.Add(new ReportCandidate(
                Guid.Parse(reader.GetString(0)),
                DateOnly.Parse(reader.GetString(1)),
                DateOnly.Parse(reader.GetString(2)),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetDouble(7),
                reader.GetInt64(8) == 1,
                reader.GetInt64(9) == 1,
                sourceIds,
                reader.GetInt64(11) == 1,
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.GetString(13),
                reader.GetString(14)));
        }

        return candidates;
    }

    private static string EscapeLike(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static async Task InsertAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ReportCandidate candidate,
        bool ignoreConflict,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = $"""
            INSERT {(ignoreConflict ? "OR IGNORE " : string.Empty)}INTO report_candidates
                (id, week_start, work_date, work_item, activity, result_or_next,
                 status, confidence, selected, edited, source_event_ids_json,
                 needs_confirmation, confirmation_question, origin, category)
            VALUES
                ($id, $weekStart, $workDate, $workItem, $activity, $result,
                 $status, $confidence, $selected, $edited, $sourceIds,
                 $needsConfirmation, $confirmationQuestion, $origin, $category);
            """;
        insert.Parameters.AddWithValue("$id", candidate.Id.ToString("D"));
        insert.Parameters.AddWithValue("$weekStart", candidate.WeekStart.ToString("yyyy-MM-dd"));
        insert.Parameters.AddWithValue("$workDate", candidate.WorkDate.ToString("yyyy-MM-dd"));
        insert.Parameters.AddWithValue("$workItem", candidate.WorkItem);
        insert.Parameters.AddWithValue("$activity", candidate.Activity);
        insert.Parameters.AddWithValue("$result", candidate.ResultOrNext);
        insert.Parameters.AddWithValue("$status", candidate.Status);
        insert.Parameters.AddWithValue("$confidence", candidate.Confidence);
        insert.Parameters.AddWithValue("$selected", candidate.Selected ? 1 : 0);
        insert.Parameters.AddWithValue("$edited", candidate.Edited ? 1 : 0);
        insert.Parameters.AddWithValue("$sourceIds", JsonSerializer.Serialize(candidate.SourceEventIds));
        insert.Parameters.AddWithValue("$needsConfirmation", candidate.NeedsConfirmation ? 1 : 0);
        insert.Parameters.AddWithValue(
            "$confirmationQuestion",
            (object?)candidate.ConfirmationQuestion ?? DBNull.Value);
        insert.Parameters.AddWithValue("$origin", candidate.Origin);
        insert.Parameters.AddWithValue("$category", candidate.Category);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }
}
