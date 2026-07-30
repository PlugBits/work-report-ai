using WorkLogAI.Core;
using WorkLogAI.Infrastructure;

namespace WorkLogAI.Tests;

public sealed class Phase2PersistenceTests
{
    [Fact]
    public async Task Source_events_are_deduplicated_by_stable_content_hash()
    {
        using var temporary = new TemporaryDirectory();
        var factory = await CreateDatabaseAsync(temporary, "events.db");
        var repository = new SqliteSourceEventRepository(factory);
        var occurredAt = new DateTimeOffset(2026, 7, 30, 10, 0, 0, TimeSpan.FromHours(-4));
        var first = SourceEventFactory.Create(
            occurredAt,
            SourceTypes.Git,
            "commit",
            "changed: README.md",
            "commit=abc",
            "git:repo:abc",
            .9,
            occurredAt);
        var sameContent = first with
        {
            Id = Guid.NewGuid(),
            CollectedAt = occurredAt.AddHours(2)
        };

        Assert.True(await repository.InsertIfNewAsync(first));
        Assert.False(await repository.InsertIfNewAsync(sameContent));
        var events = await repository.ListAsync(
            new WeekRange(new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 2)));
        Assert.Equal(first.Id, Assert.Single(events).Id);
        Assert.Equal(64, first.ContentHash.Length);
    }

    [Fact]
    public void Local_mapping_preserves_evidence_and_never_invents_completion_or_results()
    {
        var source = SourceEventFactory.Create(
            new DateTimeOffset(2026, 7, 30, 11, 0, 0, TimeSpan.FromHours(-4)),
            SourceTypes.Codex,
            "Codex session",
            "implemented local parser",
            "cwd=C:\\work",
            "codex:session",
            .75);

        var candidate = new LocalSourceEventMapper().Map(source, new DateOnly(2026, 7, 27));

        Assert.Equal("pending", candidate.Status);
        Assert.Equal(string.Empty, candidate.ResultOrNext);
        Assert.False(candidate.Selected);
        Assert.Equal([source.Id], candidate.SourceEventIds);
        Assert.Contains("implemented local parser", candidate.Activity);
    }

    [Theory]
    [InlineData(SourceTypes.OutlookMail, "メール対応")]
    [InlineData(SourceTypes.Calendar, "会議・予定")]
    public void Local_mapping_labels_graph_sources_and_keeps_calendar_pending(
        string sourceType,
        string expectedWorkItem)
    {
        var source = SourceEventFactory.Create(
            new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.FromHours(-4)),
            sourceType,
            "件名",
            "本文",
            "宛先: 田中太郎",
            "graph:1",
            sourceType == SourceTypes.OutlookMail ? .7 : .5);

        var candidate = new LocalSourceEventMapper().Map(source, new DateOnly(2026, 7, 27));

        Assert.Equal(expectedWorkItem, candidate.WorkItem);
        Assert.Equal("pending", candidate.Status);
        Assert.Equal(string.Empty, candidate.ResultOrNext);
    }

    [Fact]
    public async Task Candidate_replace_is_transactional_and_idempotent_with_json_evidence()
    {
        using var temporary = new TemporaryDirectory();
        var factory = await CreateDatabaseAsync(temporary, "candidates.db");
        var repository = new SqliteReportCandidateRepository(factory);
        var sourceId = Guid.NewGuid();
        var candidate = new ReportCandidate(
            Guid.NewGuid(),
            new DateOnly(2026, 7, 27),
            new DateOnly(2026, 7, 30),
            "ローカルGit",
            "metadata only",
            "",
            "pending",
            .9,
            false,
            false,
            [sourceId]);

        await repository.ReplaceWeekAsync(candidate.WeekStart, [candidate]);
        await repository.ReplaceWeekAsync(candidate.WeekStart, [candidate]);

        var loaded = Assert.Single(await repository.ListAsync(candidate.WeekStart));
        Assert.Equal(candidate.Id, loaded.Id);
        Assert.Equal([sourceId], loaded.SourceEventIds);
    }

    [Fact]
    public async Task Coordinator_repeated_run_deduplicates_and_preserves_success_when_one_source_fails()
    {
        using var temporary = new TemporaryDirectory();
        var factory = await CreateDatabaseAsync(temporary, "coordinator.db");
        var sources = new SqliteSourceEventRepository(factory);
        var candidates = new SqliteReportCandidateRepository(factory);
        var range = new WeekRange(new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 2));
        var sourceEvent = SourceEventFactory.Create(
            new DateTimeOffset(2026, 7, 30, 10, 0, 0, TimeSpan.FromHours(-4)),
            SourceTypes.File,
            "report.xlsx",
            "metadata",
            "path=report.xlsx",
            "file:report.xlsx",
            .45);
        var coordinator = new LocalCollectionCoordinator(
            [new FixedCollector(sourceEvent), new FailingCollector()],
            sources,
            candidates,
            new LocalSourceEventMapper());

        var first = await coordinator.RunAsync(range);
        var second = await coordinator.RunAsync(range);

        Assert.Equal(1, first.InsertedEvents);
        Assert.Equal(0, second.InsertedEvents);
        Assert.Single(await candidates.ListAsync(range.Start));
        Assert.Single(first.Errors);
        Assert.Equal(sourceEvent.Id, Assert.Single(
            (await candidates.ListAsync(range.Start))[0].SourceEventIds));
    }

    [Fact]
    public async Task Sample_seed_is_isolated_by_caller_path_and_idempotent_for_events_and_candidates()
    {
        using var temporary = new TemporaryDirectory();
        var factory = await CreateDatabaseAsync(temporary, "sample.db");
        var notes = new SqliteQuickNoteRepository(factory);
        var sources = new SqliteSourceEventRepository(factory);
        var candidates = new SqliteReportCandidateRepository(factory);
        var seeder = new SampleDataSeeder(notes, sources, candidates);

        await seeder.SeedIfEmptyAsync();
        await seeder.SeedIfEmptyAsync();

        var now = DateTimeOffset.Now;
        Assert.Equal(3, (await notes.ListAsync(now.AddYears(-1), now.AddYears(1), true)).Count);
        var week = new WeekRangeCalculator().GetWeekRange(DateOnly.FromDateTime(now.DateTime));
        Assert.Single(await sources.ListAsync(week));
        Assert.Single(await candidates.ListAsync(week.Start));
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

    private sealed class FixedCollector(SourceEvent sourceEvent) : ISourceCollector
    {
        public string SourceName => "fixed";

        public Task<SourceCollectionResult> CollectAsync(
            WeekRange range,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SourceCollectionResult(SourceName, [sourceEvent], []));
    }

    private sealed class FailingCollector : ISourceCollector
    {
        public string SourceName => "failure";

        public Task<SourceCollectionResult> CollectAsync(
            WeekRange range,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("expected failure");
    }
}
