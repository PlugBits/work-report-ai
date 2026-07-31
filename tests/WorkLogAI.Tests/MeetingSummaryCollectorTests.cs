using WorkLogAI.Core;
using WorkLogAI.Infrastructure;

namespace WorkLogAI.Tests;

public sealed class MeetingSummaryCollectorTests
{
    private static readonly WeekRange Range = new(new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 2));

    [Fact]
    public async Task Collector_maps_formatted_sessions_to_source_events_without_raw_lines()
    {
        using var temporary = new TemporaryDirectory();
        var factory = await CreateDatabaseAsync(temporary, "collector-map.db");
        var repository = new SqliteMeetingRepository(factory);
        var startedAt = new DateTimeOffset(2026, 7, 29, 14, 30, 0, TimeSpan.FromHours(9));
        var session = await repository.CreateSessionAsync("定例会議", "田中、鈴木", MeetingKind.Meeting, startedAt);
        await repository.AddLineAsync(session.Id, MeetingMarker.Todo, "パスワードp@ss1234を控える");
        await repository.SaveSummaryAsync(session.Id, """{"summaryLine":"要約"}""", "来期予算を承認し資料送付を宿題とした。");

        var collector = new MeetingSummaryCollector(repository);
        var result = await collector.CollectAsync(Range);

        var sourceEvent = Assert.Single(result.Events);
        Assert.Equal(SourceTypes.Meeting, sourceEvent.SourceType);
        Assert.Equal(startedAt, sourceEvent.OccurredAt);
        Assert.Equal("定例会議", sourceEvent.Title);
        Assert.Equal("来期予算を承認し資料送付を宿題とした。", sourceEvent.Body);
        Assert.Equal($"{startedAt:yyyy-MM-dd HH:mm} 定例会議", sourceEvent.Evidence);
        Assert.Equal($"meeting:{session.Id:D}", sourceEvent.SourceRef);
        Assert.Equal(0.8, sourceEvent.Confidence);
        Assert.DoesNotContain("パスワード", sourceEvent.Body);
        Assert.DoesNotContain("p@ss1234", sourceEvent.Body);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task Empty_range_returns_no_events()
    {
        using var temporary = new TemporaryDirectory();
        var factory = await CreateDatabaseAsync(temporary, "collector-empty.db");
        var repository = new SqliteMeetingRepository(factory);
        var collector = new MeetingSummaryCollector(repository);

        var result = await collector.CollectAsync(Range);

        Assert.Empty(result.Events);
        Assert.Equal("meeting", result.SourceName);
    }

    [Fact]
    public async Task Only_formatted_sessions_are_included()
    {
        using var temporary = new TemporaryDirectory();
        var factory = await CreateDatabaseAsync(temporary, "collector-only-formatted.db");
        var repository = new SqliteMeetingRepository(factory);
        var startedAt = new DateTimeOffset(2026, 7, 29, 9, 0, 0, TimeSpan.FromHours(9));
        await repository.CreateSessionAsync("下書き会議", "", MeetingKind.Meeting, startedAt);
        var closedButNotFormatted = await repository.CreateSessionAsync(
            "終了済み未整形", "", MeetingKind.Meeting, startedAt.AddHours(1));
        await repository.UpdateSessionAsync(
            closedButNotFormatted.Id,
            "終了済み未整形",
            "",
            MeetingKind.Meeting,
            MeetingStatus.Closed,
            startedAt.AddHours(2));

        var collector = new MeetingSummaryCollector(repository);
        var result = await collector.CollectAsync(Range);

        Assert.Empty(result.Events);
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
