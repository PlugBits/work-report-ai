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
    public async Task DeleteByIdsAsync_removes_only_targeted_events_and_tolerates_missing_or_empty_ids()
    {
        using var temporary = new TemporaryDirectory();
        var factory = await CreateDatabaseAsync(temporary, "delete-events.db");
        var repository = new SqliteSourceEventRepository(factory);
        var occurredAt = new DateTimeOffset(2026, 7, 30, 10, 0, 0, TimeSpan.FromHours(-4));
        var keep = SourceEventFactory.Create(
            occurredAt,
            SourceTypes.Git,
            "commit-keep",
            "changed: keep.txt",
            "commit=keep",
            "git:repo:keep",
            .9,
            occurredAt);
        var removeFirst = SourceEventFactory.Create(
            occurredAt,
            SourceTypes.Manual,
            "manual-1",
            "manual row 1",
            "manual",
            "review-manual:1",
            1,
            occurredAt);
        var removeSecond = SourceEventFactory.Create(
            occurredAt,
            SourceTypes.Manual,
            "manual-2",
            "manual row 2",
            "manual",
            "review-manual:2",
            1,
            occurredAt);
        Assert.True(await repository.InsertIfNewAsync(keep));
        Assert.True(await repository.InsertIfNewAsync(removeFirst));
        Assert.True(await repository.InsertIfNewAsync(removeSecond));

        Assert.Equal(0, await repository.DeleteByIdsAsync([]));
        Assert.Equal(0, await repository.DeleteByIdsAsync([Guid.NewGuid()]));

        var deleted = await repository.DeleteByIdsAsync(
            [removeFirst.Id, removeSecond.Id, Guid.NewGuid()]);

        Assert.Equal(2, deleted);
        var remaining = await repository.ListAsync(
            new WeekRange(new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 2)));
        Assert.Equal(keep.Id, Assert.Single(remaining).Id);
    }

    [Fact]
    public async Task ListIdsOlderThanAsync_returns_only_events_strictly_before_the_cutoff()
    {
        using var temporary = new TemporaryDirectory();
        var factory = await CreateDatabaseAsync(temporary, "older-than.db");
        var repository = new SqliteSourceEventRepository(factory);
        var cutoff = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var older = SourceEventFactory.Create(
            cutoff.AddDays(-1),
            SourceTypes.Manual,
            "old",
            "old row",
            "manual",
            "review-manual:old",
            1,
            cutoff.AddDays(-1));
        var atCutoff = SourceEventFactory.Create(
            cutoff,
            SourceTypes.Manual,
            "at-cutoff",
            "at cutoff row",
            "manual",
            "review-manual:at-cutoff",
            1,
            cutoff);
        var newer = SourceEventFactory.Create(
            cutoff.AddDays(1),
            SourceTypes.Manual,
            "new",
            "new row",
            "manual",
            "review-manual:new",
            1,
            cutoff.AddDays(1));
        await repository.InsertIfNewAsync(older);
        await repository.InsertIfNewAsync(atCutoff);
        await repository.InsertIfNewAsync(newer);

        var oldIds = await repository.ListIdsOlderThanAsync(cutoff);

        Assert.Equal([older.Id], oldIds);
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
        Assert.Equal(ReportCategories.Internal, candidate.Category);
    }

    [Fact]
    public void Local_mapping_strips_the_git_file_list_from_activity_but_keeps_subject_and_statistics()
    {
        var source = SourceEventFactory.Create(
            new DateTimeOffset(2026, 7, 30, 11, 0, 0, TimeSpan.FromHours(-4)),
            SourceTypes.Git,
            "feat: 検査結果の入力を追加",
            "検査結果入力画面を実装。変更ファイル: src/Inspection.cs, src/Inspection.Tests.cs。統計: +40 / -5",
            "repository=C:\\work\\repo; commit=abc123; files=src/Inspection.cs,src/Inspection.Tests.cs",
            "git:repo:abc123",
            .9);

        var candidate = new LocalSourceEventMapper().Map(source, new DateOnly(2026, 7, 27));

        Assert.Equal("ローカルGit", candidate.WorkItem);
        Assert.Contains("feat: 検査結果の入力を追加", candidate.Activity);
        Assert.Contains("検査結果入力画面を実装", candidate.Activity);
        Assert.Contains("統計: +40 / -5", candidate.Activity);
        Assert.DoesNotContain("変更ファイル", candidate.Activity);
        Assert.DoesNotContain("Inspection.cs", candidate.Activity);
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
    public void Local_mapping_treats_a_formatted_meeting_as_completed_and_pre_selected()
    {
        var source = SourceEventFactory.Create(
            new DateTimeOffset(2026, 7, 30, 10, 0, 0, TimeSpan.FromHours(9)),
            SourceTypes.Meeting,
            "定例会議",
            "定例会議で来期予算を承認し資料送付を宿題とした。",
            "2026-07-30 10:00 定例会議",
            "meeting:11111111-1111-1111-1111-111111111111",
            .8);

        var candidate = new LocalSourceEventMapper().Map(source, new DateOnly(2026, 7, 27));

        Assert.Equal("会議・打合せ", candidate.WorkItem);
        Assert.Equal("completed", candidate.Status);
        Assert.True(candidate.Selected);
        Assert.Equal(source.Body, candidate.Activity);
        Assert.Equal([source.Id], candidate.SourceEventIds);
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
    public async Task Candidate_category_round_trips_for_internal_and_external_rows()
    {
        using var temporary = new TemporaryDirectory();
        var factory = await CreateDatabaseAsync(temporary, "candidate-category.db");
        var repository = new SqliteReportCandidateRepository(factory);
        var weekStart = new DateOnly(2026, 7, 27);
        var internalCandidate = new ReportCandidate(
            Guid.NewGuid(),
            weekStart,
            new DateOnly(2026, 7, 28),
            "社内案件",
            "社内活動",
            "",
            "pending",
            .9,
            true,
            false,
            [Guid.NewGuid()]);
        var externalCandidate = new ReportCandidate(
            Guid.NewGuid(),
            weekStart,
            new DateOnly(2026, 7, 29),
            "社外案件",
            "社外活動",
            "",
            "pending",
            .9,
            true,
            false,
            [Guid.NewGuid()],
            Category: ReportCategories.External);

        await repository.ReplaceWeekAsync(weekStart, [internalCandidate, externalCandidate]);
        var loaded = (await repository.ListAsync(weekStart)).ToDictionary(item => item.Id);

        Assert.Equal(ReportCategories.Internal, loaded[internalCandidate.Id].Category);
        Assert.Equal(ReportCategories.External, loaded[externalCandidate.Id].Category);
    }

    [Fact]
    public async Task SetSelectedAsync_updates_only_the_selected_column_for_targeted_ids()
    {
        using var temporary = new TemporaryDirectory();
        var factory = await CreateDatabaseAsync(temporary, "candidate-selected.db");
        var repository = new SqliteReportCandidateRepository(factory);
        var weekStart = new DateOnly(2026, 7, 27);
        var target = new ReportCandidate(
            Guid.NewGuid(), weekStart, new DateOnly(2026, 7, 28), "対象", "対象活動",
            "", "pending", .8, true, false, [Guid.NewGuid()]);
        var untouched = new ReportCandidate(
            Guid.NewGuid(), weekStart, new DateOnly(2026, 7, 29), "対象外", "対象外活動",
            "", "pending", .8, true, false, [Guid.NewGuid()]);
        await repository.ReplaceWeekAsync(weekStart, [target, untouched]);

        Assert.Equal(0, await repository.SetSelectedAsync([], false));
        var updated = await repository.SetSelectedAsync([target.Id], false);
        var loaded = (await repository.ListAsync(weekStart)).ToDictionary(item => item.Id);

        Assert.Equal(1, updated);
        Assert.False(loaded[target.Id].Selected);
        Assert.True(loaded[untouched.Id].Selected);
        // Every other column is untouched by the selection-only update.
        Assert.Equal(target.Activity, loaded[target.Id].Activity);
        Assert.False(loaded[target.Id].Edited);

        var restored = await repository.SetSelectedAsync([target.Id, untouched.Id], true);
        var reloaded = (await repository.ListAsync(weekStart)).ToDictionary(item => item.Id);
        Assert.Equal(2, restored);
        Assert.True(reloaded[target.Id].Selected);
        Assert.True(reloaded[untouched.Id].Selected);
    }

    [Fact]
    public async Task ListSelectedByDateRangeAsync_spans_multiple_weeks_and_excludes_unselected_and_out_of_range_rows()
    {
        using var temporary = new TemporaryDirectory();
        var factory = await CreateDatabaseAsync(temporary, "monthly-range.db");
        var repository = new SqliteReportCandidateRepository(factory);
        var julyWeekStart = new DateOnly(2026, 6, 29);
        var augustWeekStart = new DateOnly(2026, 7, 27);
        var inRangeEarly = new ReportCandidate(
            Guid.NewGuid(), julyWeekStart, new DateOnly(2026, 7, 2), "案件A", "活動A",
            "", "pending", .8, true, false, [Guid.NewGuid()]);
        var inRangeLate = new ReportCandidate(
            Guid.NewGuid(), augustWeekStart, new DateOnly(2026, 7, 30), "案件B", "活動B",
            "", "pending", .8, true, false, [Guid.NewGuid()]);
        var unselected = new ReportCandidate(
            Guid.NewGuid(), julyWeekStart, new DateOnly(2026, 7, 15), "案件C", "活動C",
            "", "pending", .8, false, false, [Guid.NewGuid()]);
        var outOfRange = new ReportCandidate(
            Guid.NewGuid(), augustWeekStart, new DateOnly(2026, 8, 1), "案件D", "活動D",
            "", "pending", .8, true, false, [Guid.NewGuid()]);
        await repository.ReplaceWeekAsync(julyWeekStart, [inRangeEarly, unselected]);
        await repository.ReplaceWeekAsync(augustWeekStart, [inRangeLate, outOfRange]);

        var result = await repository.ListSelectedByDateRangeAsync(
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 31));

        Assert.Equal([inRangeEarly.Id, inRangeLate.Id], result.Select(item => item.Id).ToArray());
    }

    [Fact]
    public async Task ListAllSourceEventIdJsonAsync_returns_every_candidates_raw_source_id_json()
    {
        using var temporary = new TemporaryDirectory();
        var factory = await CreateDatabaseAsync(temporary, "raw-source-ids.db");
        var repository = new SqliteReportCandidateRepository(factory);
        var weekStart = new DateOnly(2026, 7, 27);
        var sourceId = Guid.NewGuid();
        var candidate = new ReportCandidate(
            Guid.NewGuid(), weekStart, new DateOnly(2026, 7, 28), "案件", "活動",
            "", "pending", .8, true, false, [sourceId]);
        await repository.ReplaceWeekAsync(weekStart, [candidate]);

        var rawJsonValues = await repository.ListAllSourceEventIdJsonAsync();

        Assert.Contains(rawJsonValues, json => json.Contains(sourceId.ToString(), StringComparison.OrdinalIgnoreCase));
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
    public async Task Coordinator_maps_only_the_latest_event_per_stable_source_ref_into_candidates()
    {
        using var temporary = new TemporaryDirectory();
        var factory = await CreateDatabaseAsync(temporary, "coordinator-dedup.db");
        var sources = new SqliteSourceEventRepository(factory);
        var candidates = new SqliteReportCandidateRepository(factory);
        var range = new WeekRange(new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 2));
        var occurredAt = new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.FromHours(-4));
        // Simulates meeting re-formatting: same sourceRef, different content, both
        // already sitting in source_events (stored events are never touched here).
        var stale = SourceEventFactory.Create(
            occurredAt, SourceTypes.Meeting, "meeting v1", "stale summary", "evidence",
            "meeting:stable-ref", .9, occurredAt);
        var fresh = SourceEventFactory.Create(
            occurredAt, SourceTypes.Meeting, "meeting v2", "fresh summary", "evidence",
            "meeting:stable-ref", .9, occurredAt.AddHours(2));
        Assert.True(await sources.InsertIfNewAsync(stale));
        Assert.True(await sources.InsertIfNewAsync(fresh));
        var coordinator = new LocalCollectionCoordinator([], sources, candidates, new LocalSourceEventMapper());

        var result = await coordinator.RunAsync(range);
        var loaded = Assert.Single(await candidates.ListAsync(range.Start));

        Assert.Equal(1, result.CandidateCount);
        Assert.Equal(fresh.Id, Assert.Single(loaded.SourceEventIds));
        Assert.Contains("fresh summary", loaded.Activity);
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
