using System.Globalization;
using Microsoft.Data.Sqlite;
using WorkLogAI.Core;

namespace WorkLogAI.Infrastructure;

public sealed class SqliteMeetingRepository(SqliteConnectionFactory connectionFactory)
    : IMeetingRepository
{
    public async Task<MeetingSession> CreateSessionAsync(
        string title,
        string participants,
        MeetingKind kind,
        DateTimeOffset? startedAt = null,
        CancellationToken cancellationToken = default)
    {
        var session = new MeetingSession(
            Guid.NewGuid(),
            title ?? string.Empty,
            participants ?? string.Empty,
            kind,
            startedAt ?? DateTimeOffset.Now,
            null,
            MeetingStatus.Draft,
            DateTimeOffset.Now);

        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO meeting_sessions (id, title, participants, kind, started_at, ended_at, status, created_at)
            VALUES ($id, $title, $participants, $kind, $startedAt, NULL, $status, $createdAt);
            """;
        command.Parameters.AddWithValue("$id", session.Id.ToString("D"));
        command.Parameters.AddWithValue("$title", session.Title);
        command.Parameters.AddWithValue("$participants", session.Participants);
        command.Parameters.AddWithValue("$kind", MeetingKindStrings.ToStorageString(session.Kind));
        command.Parameters.AddWithValue("$startedAt", FormatTimestamp(session.StartedAt));
        command.Parameters.AddWithValue("$status", MeetingStatusStrings.ToStorageString(session.Status));
        command.Parameters.AddWithValue("$createdAt", FormatTimestamp(session.CreatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return session;
    }

    public async Task UpdateSessionAsync(
        Guid sessionId,
        string title,
        string participants,
        MeetingKind kind,
        MeetingStatus status,
        DateTimeOffset? endedAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE meeting_sessions
            SET title = $title,
                participants = $participants,
                kind = $kind,
                status = $status,
                ended_at = $endedAt
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", sessionId.ToString("D"));
        command.Parameters.AddWithValue("$title", title ?? string.Empty);
        command.Parameters.AddWithValue("$participants", participants ?? string.Empty);
        command.Parameters.AddWithValue("$kind", MeetingKindStrings.ToStorageString(kind));
        command.Parameters.AddWithValue("$status", MeetingStatusStrings.ToStorageString(status));
        command.Parameters.AddWithValue(
            "$endedAt",
            endedAt is null ? DBNull.Value : FormatTimestamp(endedAt.Value));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MeetingSession>> ListSessionsAsync(
        MeetingStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var sessions = new List<MeetingSession>();
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = status is null
            ? """
                SELECT id, title, participants, kind, started_at, ended_at, status, created_at
                FROM meeting_sessions
                ORDER BY julianday(started_at) DESC, id;
                """
            : """
                SELECT id, title, participants, kind, started_at, ended_at, status, created_at
                FROM meeting_sessions
                WHERE status = $status
                ORDER BY julianday(started_at) DESC, id;
                """;
        if (status is not null)
        {
            command.Parameters.AddWithValue("$status", MeetingStatusStrings.ToStorageString(status.Value));
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            sessions.Add(ReadSession(reader));
        }

        return sessions;
    }

    public async Task<MeetingSession?> GetSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, title, participants, kind, started_at, ended_at, status, created_at
            FROM meeting_sessions
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", sessionId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSession(reader) : null;
    }

    public async Task<MeetingLine> AddLineAsync(
        Guid sessionId,
        MeetingMarker marker,
        string text,
        DateTimeOffset? loggedAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var nextLineNo = await GetNextLineNoAsync(connection, transaction, sessionId, cancellationToken);
        var line = new MeetingLine(
            Guid.NewGuid(),
            sessionId,
            nextLineNo,
            marker,
            text.Trim(),
            loggedAt ?? DateTimeOffset.Now);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO meeting_lines (id, session_id, line_no, marker, text, logged_at)
                VALUES ($id, $sessionId, $lineNo, $marker, $text, $loggedAt);
                """;
            command.Parameters.AddWithValue("$id", line.Id.ToString("D"));
            command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
            command.Parameters.AddWithValue("$lineNo", line.LineNo);
            command.Parameters.AddWithValue("$marker", MeetingMarkerStrings.ToStorageString(line.Marker));
            command.Parameters.AddWithValue("$text", line.Text);
            command.Parameters.AddWithValue("$loggedAt", FormatTimestamp(line.LoggedAt));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return line;
    }

    public async Task UpdateLineAsync(
        Guid lineId,
        MeetingMarker marker,
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE meeting_lines
            SET marker = $marker, text = $text
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", lineId.ToString("D"));
        command.Parameters.AddWithValue("$marker", MeetingMarkerStrings.ToStorageString(marker));
        command.Parameters.AddWithValue("$text", text.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteLineAsync(Guid lineId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM meeting_lines WHERE id = $id;";
        command.Parameters.AddWithValue("$id", lineId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MeetingLine>> ListLinesAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var lines = new List<MeetingLine>();
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, session_id, line_no, marker, text, logged_at
            FROM meeting_lines
            WHERE session_id = $sessionId
            ORDER BY line_no;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            lines.Add(new MeetingLine(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetInt32(2),
                MeetingMarkerStrings.Parse(reader.GetString(3)),
                reader.GetString(4),
                DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture)));
        }

        return lines;
    }

    public async Task SaveSummaryAsync(
        Guid sessionId,
        string formattedJson,
        string summaryLine,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formattedJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(summaryLine);

        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText = """
                INSERT INTO meeting_summaries (id, session_id, formatted_json, summary_line, created_at)
                VALUES ($id, $sessionId, $formattedJson, $summaryLine, $createdAt);
                """;
            insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
            insert.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
            insert.Parameters.AddWithValue("$formattedJson", formattedJson);
            insert.Parameters.AddWithValue("$summaryLine", summaryLine.Trim());
            insert.Parameters.AddWithValue("$createdAt", FormatTimestamp(DateTimeOffset.Now));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = (SqliteTransaction)transaction;
            update.CommandText = "UPDATE meeting_sessions SET status = $status WHERE id = $id;";
            update.Parameters.AddWithValue("$status", MeetingStatusStrings.ToStorageString(MeetingStatus.Formatted));
            update.Parameters.AddWithValue("$id", sessionId.ToString("D"));
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<MeetingSummary?> GetLatestSummaryAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, session_id, formatted_json, summary_line, created_at
            FROM meeting_summaries
            WHERE session_id = $sessionId
            ORDER BY julianday(created_at) DESC, id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSummary(reader) : null;
    }

    public async Task<IReadOnlyList<(MeetingSession Session, MeetingSummary Summary)>> ListFormattedInRangeAsync(
        WeekRange range,
        CancellationToken cancellationToken = default)
    {
        var results = new List<(MeetingSession, MeetingSummary)>();
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.id, s.title, s.participants, s.kind, s.started_at, s.ended_at, s.status, s.created_at,
                   m.id, m.session_id, m.formatted_json, m.summary_line, m.created_at
            FROM meeting_sessions s
            JOIN meeting_summaries m ON m.session_id = s.id
            WHERE s.status = $status
              AND substr(s.started_at, 1, 10) >= $start
              AND substr(s.started_at, 1, 10) <= $end
              AND m.created_at = (
                  SELECT MAX(m2.created_at) FROM meeting_summaries m2 WHERE m2.session_id = s.id
              )
            ORDER BY s.started_at, s.id;
            """;
        command.Parameters.AddWithValue("$status", MeetingStatusStrings.ToStorageString(MeetingStatus.Formatted));
        command.Parameters.AddWithValue("$start", range.Start.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$end", range.End.ToString("yyyy-MM-dd"));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var session = ReadSession(reader);
            var summary = new MeetingSummary(
                Guid.Parse(reader.GetString(8)),
                Guid.Parse(reader.GetString(9)),
                reader.GetString(10),
                reader.GetString(11),
                DateTimeOffset.Parse(reader.GetString(12), CultureInfo.InvariantCulture));
            results.Add((session, summary));
        }

        return results;
    }

    public async Task<DateOnly?> GetEarliestStartedDateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MIN(started_at) FROM meeting_sessions;";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is string text
            ? DateOnly.FromDateTime(DateTimeOffset.Parse(text, CultureInfo.InvariantCulture).DateTime)
            : null;
    }

    private static MeetingSummary ReadSummary(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        Guid.Parse(reader.GetString(1)),
        reader.GetString(2),
        reader.GetString(3),
        DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture));

    private static async Task<int> GetNextLineNoAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "SELECT COALESCE(MAX(line_no), 0) + 1 FROM meeting_lines WHERE session_id = $sessionId;";
        command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    private static MeetingSession ReadSession(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        reader.GetString(1),
        reader.GetString(2),
        MeetingKindStrings.Parse(reader.GetString(3)),
        DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
        reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
        MeetingStatusStrings.Parse(reader.GetString(6)),
        DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture));

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);
}
