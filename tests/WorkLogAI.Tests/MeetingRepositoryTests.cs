using WorkLogAI.Core;
using WorkLogAI.Infrastructure;

namespace WorkLogAI.Tests;

public sealed class MeetingRepositoryTests
{
    [Fact]
    public async Task Create_list_and_get_session_round_trip()
    {
        using var temporary = new TemporaryDirectory();
        var factory = await CreateDatabaseAsync(temporary, "sessions.db");
        var repository = new SqliteMeetingRepository(factory);
        var startedAt = new DateTimeOffset(2026, 7, 31, 9, 0, 0, TimeSpan.FromHours(9));

        var created = await repository.CreateSessionAsync("定例会議", "田中、鈴木", MeetingKind.Meeting, startedAt);

        Assert.Equal(MeetingStatus.Draft, created.Status);
        Assert.Null(created.EndedAt);

        var fetched = await repository.GetSessionAsync(created.Id);
        Assert.NotNull(fetched);
        Assert.Equal("定例会議", fetched!.Title);
        Assert.Equal("田中、鈴木", fetched.Participants);
        Assert.Equal(MeetingKind.Meeting, fetched.Kind);
        Assert.Equal(startedAt, fetched.StartedAt);

        var drafts = await repository.ListSessionsAsync(MeetingStatus.Draft);
        Assert.Single(drafts);
        Assert.Equal(created.Id, drafts[0].Id);

        Assert.Empty(await repository.ListSessionsAsync(MeetingStatus.Closed));
    }

    [Fact]
    public async Task Update_session_changes_header_fields_status_and_ended_at()
    {
        using var temporary = new TemporaryDirectory();
        var factory = await CreateDatabaseAsync(temporary, "update-session.db");
        var repository = new SqliteMeetingRepository(factory);
        var created = await repository.CreateSessionAsync("", "", MeetingKind.Meeting, DateTimeOffset.Now);
        var endedAt = DateTimeOffset.Now;

        await repository.UpdateSessionAsync(
            created.Id,
            "改訂タイトル",
            "佐藤",
            MeetingKind.Visitor,
            MeetingStatus.Closed,
            endedAt);

        var updated = await repository.GetSessionAsync(created.Id);
        Assert.NotNull(updated);
        Assert.Equal("改訂タイトル", updated!.Title);
        Assert.Equal("佐藤", updated.Participants);
        Assert.Equal(MeetingKind.Visitor, updated.Kind);
        Assert.Equal(MeetingStatus.Closed, updated.Status);
        Assert.Equal(endedAt, updated.EndedAt);
    }

    [Fact]
    public async Task List_sessions_orders_newest_first_by_started_at()
    {
        using var temporary = new TemporaryDirectory();
        var factory = await CreateDatabaseAsync(temporary, "order-sessions.db");
        var repository = new SqliteMeetingRepository(factory);
        var older = await repository.CreateSessionAsync(
            "older", "", MeetingKind.Meeting, new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.FromHours(9)));
        var newer = await repository.CreateSessionAsync(
            "newer", "", MeetingKind.Meeting, new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.FromHours(9)));

        var sessions = await repository.ListSessionsAsync();

        Assert.Equal(newer.Id, sessions[0].Id);
        Assert.Equal(older.Id, sessions[1].Id);
    }

    [Fact]
    public async Task Add_line_assigns_sequential_line_numbers_and_persists_marker_and_text()
    {
        using var temporary = new TemporaryDirectory();
        var factory = await CreateDatabaseAsync(temporary, "lines.db");
        var repository = new SqliteMeetingRepository(factory);
        var session = await repository.CreateSessionAsync("会議", "", MeetingKind.Meeting, DateTimeOffset.Now);

        var first = await repository.AddLineAsync(session.Id, MeetingMarker.None, "最初のメモ");
        var second = await repository.AddLineAsync(session.Id, MeetingMarker.Todo, "資料を送る");
        var third = await repository.AddLineAsync(session.Id, MeetingMarker.Decision, "予算を承認");

        Assert.Equal(1, first.LineNo);
        Assert.Equal(2, second.LineNo);
        Assert.Equal(3, third.LineNo);

        var lines = await repository.ListLinesAsync(session.Id);
        Assert.Equal(3, lines.Count);
        Assert.Equal([1, 2, 3], lines.Select(line => line.LineNo).ToArray());
        Assert.Equal(MeetingMarker.Todo, lines[1].Marker);
        Assert.Equal("資料を送る", lines[1].Text);
    }

    [Fact]
    public async Task Update_line_changes_marker_and_text_without_renumbering()
    {
        using var temporary = new TemporaryDirectory();
        var factory = await CreateDatabaseAsync(temporary, "update-line.db");
        var repository = new SqliteMeetingRepository(factory);
        var session = await repository.CreateSessionAsync("会議", "", MeetingKind.Meeting, DateTimeOffset.Now);
        var line = await repository.AddLineAsync(session.Id, MeetingMarker.None, "元のメモ");

        await repository.UpdateLineAsync(line.Id, MeetingMarker.Decision, "更新後のメモ");

        var lines = await repository.ListLinesAsync(session.Id);
        var updated = Assert.Single(lines);
        Assert.Equal(line.LineNo, updated.LineNo);
        Assert.Equal(MeetingMarker.Decision, updated.Marker);
        Assert.Equal("更新後のメモ", updated.Text);
    }

    [Fact]
    public async Task Delete_line_removes_it_and_leaves_remaining_line_numbers_stable()
    {
        using var temporary = new TemporaryDirectory();
        var factory = await CreateDatabaseAsync(temporary, "delete-line.db");
        var repository = new SqliteMeetingRepository(factory);
        var session = await repository.CreateSessionAsync("会議", "", MeetingKind.Meeting, DateTimeOffset.Now);
        var first = await repository.AddLineAsync(session.Id, MeetingMarker.None, "1つ目");
        var second = await repository.AddLineAsync(session.Id, MeetingMarker.None, "2つ目");
        var third = await repository.AddLineAsync(session.Id, MeetingMarker.None, "3つ目");

        await repository.DeleteLineAsync(second.Id);

        var lines = await repository.ListLinesAsync(session.Id);
        Assert.Equal(2, lines.Count);
        Assert.Equal([first.LineNo, third.LineNo], lines.Select(line => line.LineNo).ToArray());

        var fourth = await repository.AddLineAsync(session.Id, MeetingMarker.None, "新規");
        Assert.Equal(4, fourth.LineNo);
    }

    private static async Task<SqliteConnectionFactory> CreateDatabaseAsync(
        TemporaryDirectory temporary,
        string name)
    {
        var factory = new SqliteConnectionFactory(
            new FixedDatabasePathProvider(Path.Combine(temporary.Path, name)));
        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        return factory;
    }
}
