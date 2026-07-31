using WorkLogAI.Core;
using WorkLogAI.Infrastructure;

namespace WorkLogAI.Tests;

public sealed class DataRetentionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_deletes_old_unreferenced_source_events()
    {
        using var temporary = new TemporaryDirectory();
        var factory = new SqliteConnectionFactory(
            new FixedDatabasePathProvider(System.IO.Path.Combine(temporary.Path, "retention-unreferenced.db")));
        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        var sourceEvents = new SqliteSourceEventRepository(factory);
        var candidates = new SqliteReportCandidateRepository(factory);

        var oldEvent = CreateEvent(Now.AddDays(-200));
        await sourceEvents.InsertIfNewAsync(oldEvent);

        var deleted = await new DataRetentionService(sourceEvents, candidates).RunAsync(Now);

        Assert.Equal(1, deleted);
        Assert.Empty(await sourceEvents.GetByIdsAsync([oldEvent.Id]));
    }

    [Fact]
    public async Task RunAsync_keeps_old_source_events_still_referenced_by_a_candidate()
    {
        using var temporary = new TemporaryDirectory();
        var factory = new SqliteConnectionFactory(
            new FixedDatabasePathProvider(System.IO.Path.Combine(temporary.Path, "retention-referenced.db")));
        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        var sourceEvents = new SqliteSourceEventRepository(factory);
        var candidates = new SqliteReportCandidateRepository(factory);

        var oldEvent = CreateEvent(Now.AddDays(-200));
        await sourceEvents.InsertIfNewAsync(oldEvent);
        var weekStart = DateOnly.FromDateTime(Now.AddDays(-200).Date);
        await candidates.ReplaceWeekAsync(weekStart, [CreateCandidate(weekStart, [oldEvent.Id])]);

        var deleted = await new DataRetentionService(sourceEvents, candidates).RunAsync(Now);

        Assert.Equal(0, deleted);
        Assert.Single(await sourceEvents.GetByIdsAsync([oldEvent.Id]));
    }

    [Fact]
    public async Task RunAsync_keeps_recent_unreferenced_source_events()
    {
        using var temporary = new TemporaryDirectory();
        var factory = new SqliteConnectionFactory(
            new FixedDatabasePathProvider(System.IO.Path.Combine(temporary.Path, "retention-recent.db")));
        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        var sourceEvents = new SqliteSourceEventRepository(factory);
        var candidates = new SqliteReportCandidateRepository(factory);

        var recentEvent = CreateEvent(Now.AddDays(-10));
        await sourceEvents.InsertIfNewAsync(recentEvent);

        var deleted = await new DataRetentionService(sourceEvents, candidates).RunAsync(Now);

        Assert.Equal(0, deleted);
        Assert.Single(await sourceEvents.GetByIdsAsync([recentEvent.Id]));
    }

    [Fact]
    public async Task RunAsync_is_idempotent()
    {
        using var temporary = new TemporaryDirectory();
        var factory = new SqliteConnectionFactory(
            new FixedDatabasePathProvider(System.IO.Path.Combine(temporary.Path, "retention-idempotent.db")));
        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        var sourceEvents = new SqliteSourceEventRepository(factory);
        var candidates = new SqliteReportCandidateRepository(factory);

        var oldEvent = CreateEvent(Now.AddDays(-200));
        await sourceEvents.InsertIfNewAsync(oldEvent);
        var service = new DataRetentionService(sourceEvents, candidates);

        var firstRun = await service.RunAsync(Now);
        var secondRun = await service.RunAsync(Now);

        Assert.Equal(1, firstRun);
        Assert.Equal(0, secondRun);
    }

    private static SourceEvent CreateEvent(DateTimeOffset occurredAt) => new(
        Guid.NewGuid(),
        occurredAt,
        "manual",
        "title",
        "body",
        "evidence",
        $"ref:{Guid.NewGuid()}",
        1.0,
        Guid.NewGuid().ToString("N"),
        occurredAt);

    private static ReportCandidate CreateCandidate(DateOnly weekStart, IReadOnlyList<Guid> sourceEventIds) => new(
        Guid.NewGuid(),
        weekStart,
        weekStart,
        "手動メモ",
        "activity",
        "",
        "completed",
        1.0,
        true,
        false,
        sourceEventIds);
}
