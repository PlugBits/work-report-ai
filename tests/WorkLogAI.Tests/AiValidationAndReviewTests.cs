using ClosedXML.Excel;
using WorkLogAI.Core;
using WorkLogAI.Infrastructure;

namespace WorkLogAI.Tests;

public sealed class AiValidationAndReviewTests
{
    [Fact]
    public void Validator_accepts_only_requested_week_and_sent_evidence()
    {
        var sent = Guid.NewGuid();
        var unknown = Guid.NewGuid();
        var range = new WeekRange(new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 2));
        var envelope = new AiCandidateEnvelope
        {
            Candidates =
            [
                Payload("2026-07-30", "pending", sent),
                Payload("2026-08-10", "pending", sent),
                Payload("2026-07-30", "unsupported", sent),
                Payload("2026-07-30", "pending", unknown),
                new AiCandidatePayload
                {
                    Date = "2026-07-30",
                    WorkItem = "missing evidence",
                    Activity = "activity",
                    ResultOrNext = "",
                    Status = "pending",
                    Confidence = .5,
                    SourceEventIds = [],
                    NeedsConfirmation = false,
                    ConfirmationQuestion = null
                }
            ]
        };

        var result = new AiCandidateValidator().Validate(envelope, range, [sent]);

        var accepted = Assert.Single(result.Accepted);
        Assert.Equal([sent], accepted.SourceEventIds);
        Assert.Equal(string.Empty, accepted.ResultOrNext);
        Assert.Equal(CandidateOrigins.Ai, accepted.Origin);
        Assert.Equal(ReportCategories.Internal, accepted.Category);
        Assert.Equal(4, result.Rejections.Count);
    }

    [Fact]
    public void Dedup_merges_evidence_overlap_conservatively_and_does_not_merge_unrelated()
    {
        var common = Guid.NewGuid();
        var second = Guid.NewGuid();
        // Same-date candidates tie-break on Id in CandidateMergeService's ordering;
        // fixed ids keep "left" deterministically first so the category assertion
        // below cannot flake on random Guid ordering.
        var left = Candidate(
            new Guid("00000000-0000-0000-0000-000000000001"),
            new DateOnly(2026, 7, 29), "検査改善", "姿勢を確認",
            "completed", [common], result: "確認済み") with { Category = ReportCategories.External };
        var right = Candidate(
            new Guid("00000000-0000-0000-0000-000000000002"),
            new DateOnly(2026, 7, 29), "検査改善", "台車を試行",
            "pending", [common, second], result: "");
        var unrelated = Candidate(
            Guid.NewGuid(), new DateOnly(2026, 8, 2), "教育", "測定教育",
            "ongoing", [Guid.NewGuid()]);

        var merged = new CandidateMergeService().Merge([left, right, unrelated]);

        Assert.Equal(2, merged.Count);
        var combined = Assert.Single(merged.Where(item => item.SourceEventIds.Contains(common)));
        Assert.Equal(new[] { common, second }.Order(), combined.SourceEventIds.Order());
        Assert.Equal("pending", combined.Status);
        Assert.Contains("確認済み", combined.ResultOrNext);
        // Merge keeps the category of the first-merged (earlier-dated) item.
        Assert.Equal(ReportCategories.External, combined.Category);
    }

    [Fact]
    public async Task Regeneration_preserves_user_edits_manual_rows_and_local_candidates()
    {
        using var temporary = new TemporaryDirectory();
        var factory = new SqliteConnectionFactory(
            new FixedDatabasePathProvider(Path.Combine(temporary.Path, "regeneration.db")));
        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        var repository = new SqliteReportCandidateRepository(factory);
        var week = new DateOnly(2026, 7, 27);
        var local = Candidate(Guid.NewGuid(), week, "local", "local", "pending", [Guid.NewGuid()])
            with { Origin = CandidateOrigins.Local };
        var manual = Candidate(Guid.NewGuid(), week, "manual", "manual", "pending", [Guid.NewGuid()])
            with { Origin = CandidateOrigins.Manual, Edited = true };
        var editedAi = Candidate(Guid.NewGuid(), week, "edited AI", "kept", "ongoing", [Guid.NewGuid()])
            with { Origin = CandidateOrigins.Ai, Edited = true };
        var staleAi = Candidate(Guid.NewGuid(), week, "stale AI", "removed", "pending", [Guid.NewGuid()])
            with { Origin = CandidateOrigins.Ai };
        await repository.ReplaceWeekAsync(week, [local, manual, editedAi, staleAi]);
        var freshAi = Candidate(Guid.NewGuid(), week, "fresh AI", "new", "pending", [Guid.NewGuid()])
            with { Origin = CandidateOrigins.Ai };

        await repository.SaveGeneratedAsync(week, [freshAi]);
        var loaded = await repository.ListAsync(week);

        Assert.Contains(loaded, item => item.Id == local.Id);
        Assert.Contains(loaded, item => item.Id == manual.Id);
        Assert.Contains(loaded, item => item.Id == editedAi.Id);
        Assert.Contains(loaded, item => item.Id == freshAi.Id);
        Assert.DoesNotContain(loaded, item => item.Id == staleAi.Id);

        var changed = loaded.Select(item => item.Id == freshAi.Id
            ? item with { Selected = false, Edited = true, Activity = "user edit" }
            : item).ToArray();
        await repository.SaveReviewAsync(week, changed);
        var reviewed = await repository.ListAsync(week);
        var persisted = Assert.Single(reviewed.Where(item => item.Id == freshAi.Id));
        Assert.False(persisted.Selected);
        Assert.True(persisted.Edited);
        Assert.Equal("user edit", persisted.Activity);
    }

    [Fact]
    public void MapSelected_carries_the_candidates_category_into_the_report_row()
    {
        var internalCandidate = Candidate(
            Guid.NewGuid(), new DateOnly(2026, 7, 28), "社内案件", "社内活動", "pending", [Guid.NewGuid()]);
        var externalCandidate = Candidate(
            Guid.NewGuid(), new DateOnly(2026, 7, 29), "社外案件", "社外活動", "pending", [Guid.NewGuid()])
            with { Category = ReportCategories.External };

        var rows = new CandidateReportMapper().MapSelected([internalCandidate, externalCandidate]);

        Assert.Equal(ReportCategories.Internal, rows.Single(row => row.WorkItem == "社内案件").Category);
        Assert.Equal(ReportCategories.External, rows.Single(row => row.WorkItem == "社外案件").Category);
    }

    [Fact]
    public async Task Selected_review_rows_export_chronologically()
    {
        using var temporary = new TemporaryDirectory();
        var week = new WeekRange(new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 2));
        var candidates = new[]
        {
            Candidate(Guid.NewGuid(), new DateOnly(2026, 7, 31), "later", "later", "pending", [Guid.NewGuid()]),
            Candidate(Guid.NewGuid(), new DateOnly(2026, 7, 28), "excluded", "excluded", "pending", [Guid.NewGuid()])
                with { Selected = false },
            Candidate(Guid.NewGuid(), new DateOnly(2026, 7, 29), "earlier", "earlier", "ongoing", [Guid.NewGuid()])
        };
        var rows = new CandidateReportMapper().MapSelected(candidates);
        var path = await new ClosedXmlWeeklyReportExporter().ExportAsync(
            week, rows, temporary.Path, ReportIdentity.Default);

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet("業務週報");
        Assert.Equal("① earlier", sheet.Cell("C5").GetString());
        // 7/29's day block occupies rows 5-8 (content + 3 blanks) before the
        // next date's content row starts.
        Assert.True(sheet.Cell("C6").IsEmpty());
        Assert.True(sheet.Cell("C7").IsEmpty());
        Assert.True(sheet.Cell("C8").IsEmpty());
        Assert.Equal("① later", sheet.Cell("C9").GetString());
        Assert.True(sheet.Cell("C10").IsEmpty());
    }

    private static AiCandidatePayload Payload(string date, string status, Guid evidence) =>
        new()
        {
            Date = date,
            WorkItem = "業務",
            Activity = "活動",
            ResultOrNext = "",
            Status = status,
            Confidence = .8,
            SourceEventIds = [evidence.ToString()],
            NeedsConfirmation = false,
            ConfirmationQuestion = null
        };

    private static ReportCandidate Candidate(
        Guid id,
        DateOnly date,
        string workItem,
        string activity,
        string status,
        IReadOnlyList<Guid> evidence,
        string result = "") =>
        new(
            id,
            new WeekRangeCalculator().GetWeekRange(date).Start,
            date,
            workItem,
            activity,
            result,
            status,
            .8,
            true,
            false,
            evidence);

    private static readonly WeekRange SameDayRange =
        new(new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 2));

    [Fact]
    public void Validator_accepts_candidate_whose_evidence_all_falls_on_its_date()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var dates = new Dictionary<Guid, DateOnly>
        {
            [a] = new(2026, 7, 30),
            [b] = new(2026, 7, 30)
        };
        var payload = Payload("2026-07-30", "pending", a);
        payload.SourceEventIds.Add(b.ToString());

        var result = new AiCandidateValidator().Validate(
            new AiCandidateEnvelope { Candidates = [payload] }, SameDayRange, [a, b], dates);

        Assert.Single(result.Accepted);
        Assert.Empty(result.Rejections);
        Assert.Equal(0, result.SameDayRejectedCount);
    }

    [Fact]
    public void Validator_drops_only_the_candidate_whose_evidence_spans_two_dates()
    {
        var monday = Guid.NewGuid();
        var tuesday = Guid.NewGuid();
        var good = Guid.NewGuid();
        var dates = new Dictionary<Guid, DateOnly>
        {
            [monday] = new(2026, 7, 27),
            [tuesday] = new(2026, 7, 28),
            [good] = new(2026, 7, 29)
        };
        var spanning = Payload("2026-07-27", "pending", monday);
        spanning.SourceEventIds.Add(tuesday.ToString());

        var result = new AiCandidateValidator().Validate(
            new AiCandidateEnvelope { Candidates = [spanning, Payload("2026-07-29", "pending", good)] },
            SameDayRange,
            [monday, tuesday, good],
            dates);

        var accepted = Assert.Single(result.Accepted);
        Assert.Equal([good], accepted.SourceEventIds);
        Assert.Equal(1, result.SameDayRejectedCount);
        Assert.Equal([AiCandidateValidator.SameDayRejection], result.Rejections);
    }

    [Fact]
    public void Validator_drops_candidate_whose_date_differs_from_its_single_evidence_date()
    {
        var evidence = Guid.NewGuid();
        var dates = new Dictionary<Guid, DateOnly> { [evidence] = new(2026, 7, 28) };

        var result = new AiCandidateValidator().Validate(
            new AiCandidateEnvelope { Candidates = [Payload("2026-07-30", "pending", evidence)] },
            SameDayRange,
            [evidence],
            dates);

        Assert.Empty(result.Accepted);
        Assert.Equal(1, result.SameDayRejectedCount);
    }

    [Fact]
    public void Validator_without_event_dates_keeps_legacy_behaviour()
    {
        var evidence = Guid.NewGuid();

        var result = new AiCandidateValidator().Validate(
            new AiCandidateEnvelope { Candidates = [Payload("2026-07-30", "pending", evidence)] },
            SameDayRange,
            [evidence]);

        Assert.Single(result.Accepted);
        Assert.Equal(0, result.SameDayRejectedCount);
    }

    [Fact]
    public void Merge_service_refuses_cross_date_merge_but_still_merges_same_date()
    {
        var shared = Guid.NewGuid();
        var day1 = Candidate(Guid.NewGuid(), new DateOnly(2026, 7, 29), "検査改善", "姿勢を確認", "completed", [shared]);
        var day2 = Candidate(Guid.NewGuid(), new DateOnly(2026, 7, 30), "検査改善", "姿勢を確認", "completed", [shared]);
        var sameDay = Candidate(Guid.NewGuid(), new DateOnly(2026, 7, 29), "検査改善", "姿勢を確認", "completed", [Guid.NewGuid()]);

        Assert.Equal(2, new CandidateMergeService().Merge([day1, day2]).Count);

        var merged = new CandidateMergeService().Merge([day1, day2, sameDay]);
        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, item => item.WorkDate == new DateOnly(2026, 7, 29) && item.SourceEventIds.Count == 2);
        Assert.Contains(merged, item => item.WorkDate == new DateOnly(2026, 7, 30));
    }

    [Fact]
    public void Prompt_contains_same_day_consolidation_instruction_and_event_dates()
    {
        var source = SourceEventFactory.Create(
            new DateTimeOffset(2026, 7, 30, 23, 30, 0, TimeSpan.FromHours(9)),
            SourceTypes.Git, "t", "b", "e", "ref", .9);

        var package = new AiPromptBuilder().Build(SameDayRange, [source]);

        Assert.Contains("同一日付内のみ", package.Instructions);
        Assert.Contains("日ごとに別々のcandidate", package.Instructions);
        Assert.Contains("dateはその根拠イベントの日付と一致", package.Instructions);
        Assert.Equal(new DateOnly(2026, 7, 30), package.IncludedEventDates![source.Id]);
    }

    [Fact]
    public void Custom_instructions_are_appended_at_the_end_only_when_present()
    {
        var baseline = new AiPromptBuilder().Build(SameDayRange, []);
        var blank = new AiPromptBuilder().Build(SameDayRange, [], "  \n ");
        var custom = new AiPromptBuilder().Build(SameDayRange, [], "  語尾は「です・ます」  ");

        Assert.Equal(AiPromptBuilder.Instructions, baseline.Instructions);
        Assert.Equal(AiPromptBuilder.Instructions, blank.Instructions);
        Assert.StartsWith(AiPromptBuilder.Instructions, custom.Instructions);
        Assert.EndsWith("\n\n[ユーザー追加指示]\n語尾は「です・ます」", custom.Instructions);
        Assert.Equal(baseline.Input, custom.Input);
    }

    [Fact]
    public void Generation_and_meeting_requests_default_to_no_custom_instructions()
    {
        Assert.Equal(string.Empty, new AiGenerationRequest(SameDayRange, "m", []).CustomInstructions);
        Assert.Equal(0, new AiGenerationResult([], false, 0).SameDayRejectedCount);
        var session = new MeetingSession(
            Guid.NewGuid(), "t", "", MeetingKind.Meeting, DateTimeOffset.Now, null,
            MeetingStatus.Closed, DateTimeOffset.Now);
        Assert.Equal(string.Empty, new MeetingFormatRequest(session, [], "m").CustomInstructions);
    }
}
