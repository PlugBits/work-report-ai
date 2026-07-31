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
        Assert.Equal(4, result.Rejections.Count);
    }

    [Fact]
    public void Dedup_merges_evidence_overlap_conservatively_and_does_not_merge_unrelated()
    {
        var common = Guid.NewGuid();
        var second = Guid.NewGuid();
        var left = Candidate(
            Guid.NewGuid(), new DateOnly(2026, 7, 29), "検査改善", "姿勢を確認",
            "completed", [common], result: "確認済み");
        var right = Candidate(
            Guid.NewGuid(), new DateOnly(2026, 7, 30), "検査改善", "台車を試行",
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
        Assert.Equal("① earlier", sheet.Cell("B4").GetString());
        Assert.Equal("① later", sheet.Cell("B5").GetString());
        Assert.True(sheet.Cell("B6").IsEmpty());
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
}
