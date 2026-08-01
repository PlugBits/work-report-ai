using System.Globalization;
using Microsoft.Data.Sqlite;
using WorkLogAI.Core;

namespace WorkLogAI.Infrastructure;

public sealed class SqliteQuickNoteRepository(SqliteConnectionFactory connectionFactory)
    : IQuickNoteRepository
{
    public async Task<QuickNote> CreateAsync(
        string text,
        DateTimeOffset? createdAt = null,
        CancellationToken cancellationToken = default)
    {
        var note = new QuickNote(Guid.NewGuid(), createdAt ?? DateTimeOffset.Now, text);
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO quick_notes (id, created_at, text, deleted_at)
            VALUES ($id, $createdAt, $text, NULL);
            """;
        command.Parameters.AddWithValue("$id", note.Id.ToString("D"));
        command.Parameters.AddWithValue("$createdAt", FormatTimestamp(note.CreatedAt));
        command.Parameters.AddWithValue("$text", note.Text);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return note;
    }

    public async Task<IReadOnlyList<QuickNote>> ListAsync(
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        var notes = new List<QuickNote>();
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, created_at, text, deleted_at
            FROM quick_notes
            WHERE julianday(created_at) >= julianday($fromInclusive)
              AND julianday(created_at) < julianday($toExclusive)
              AND ($includeDeleted = 1 OR deleted_at IS NULL)
            ORDER BY julianday(created_at), id;
            """;
        command.Parameters.AddWithValue("$fromInclusive", FormatTimestamp(fromInclusive));
        command.Parameters.AddWithValue("$toExclusive", FormatTimestamp(toExclusive));
        command.Parameters.AddWithValue("$includeDeleted", includeDeleted ? 1 : 0);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            notes.Add(new QuickNote(
                Guid.Parse(reader.GetString(0)),
                DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                reader.GetString(2),
                reader.IsDBNull(3)
                    ? null
                    : DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture)));
        }

        return notes;
    }

    public Task<bool> SoftDeleteAsync(
        Guid id,
        DateTimeOffset? deletedAt = null,
        CancellationToken cancellationToken = default) =>
        SetDeletedAtAsync(id, FormatTimestamp(deletedAt ?? DateTimeOffset.Now), cancellationToken);

    public Task<bool> ReopenAsync(Guid id, CancellationToken cancellationToken = default) =>
        SetDeletedAtAsync(id, null, cancellationToken);

    public async Task<bool> UpdateTextAsync(
        Guid id,
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE quick_notes
            SET text = $text
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$text", text);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<DateOnly?> GetEarliestCreatedDateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MIN(created_at) FROM quick_notes;";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is string text
            ? DateOnly.FromDateTime(DateTimeOffset.Parse(text, CultureInfo.InvariantCulture).DateTime)
            : null;
    }

    private async Task<bool> SetDeletedAtAsync(
        Guid id,
        string? deletedAt,
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE quick_notes
            SET deleted_at = $deletedAt
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$deletedAt", (object?)deletedAt ?? DBNull.Value);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);
}
