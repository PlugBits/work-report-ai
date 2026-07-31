using WorkLogAI.Core;

namespace WorkLogAI.Tests;

public sealed class LocalCandidateSuppressorTests
{
    [Fact]
    public void Fully_covered_unedited_selected_local_row_is_superseded()
    {
        var evidence = Guid.NewGuid();
        var ai = AiCandidate([evidence]);
        var local = LocalCandidate([evidence]);

        var superseded = LocalCandidateSuppressor.SelectSupersededIds([ai], [ai, local]);

        Assert.Equal([local.Id], superseded);
    }

    [Fact]
    public void Edited_local_row_is_never_touched_even_if_fully_covered()
    {
        var evidence = Guid.NewGuid();
        var ai = AiCandidate([evidence]);
        var editedLocal = LocalCandidate([evidence]) with { Edited = true };

        var superseded = LocalCandidateSuppressor.SelectSupersededIds([ai], [ai, editedLocal]);

        Assert.Empty(superseded);
    }

    [Fact]
    public void Manual_origin_row_is_never_touched_even_if_fully_covered()
    {
        var evidence = Guid.NewGuid();
        var ai = AiCandidate([evidence]);
        var manual = LocalCandidate([evidence]) with { Origin = CandidateOrigins.Manual };

        var superseded = LocalCandidateSuppressor.SelectSupersededIds([ai], [ai, manual]);

        Assert.Empty(superseded);
    }

    [Fact]
    public void Partially_covered_local_row_stays_selected_as_a_safety_net()
    {
        var covered = Guid.NewGuid();
        var uncovered = Guid.NewGuid();
        var ai = AiCandidate([covered]);
        var local = LocalCandidate([covered, uncovered]);

        var superseded = LocalCandidateSuppressor.SelectSupersededIds([ai], [ai, local]);

        Assert.Empty(superseded);
    }

    [Fact]
    public void Already_deselected_local_row_is_not_reported_again()
    {
        var evidence = Guid.NewGuid();
        var ai = AiCandidate([evidence]);
        var local = LocalCandidate([evidence]) with { Selected = false };

        var superseded = LocalCandidateSuppressor.SelectSupersededIds([ai], [ai, local]);

        Assert.Empty(superseded);
    }

    [Fact]
    public void Ai_and_other_non_local_origin_rows_in_the_week_are_never_candidates_for_suppression()
    {
        var evidence = Guid.NewGuid();
        var ai = AiCandidate([evidence]);
        var otherAi = AiCandidate([evidence]);
        var unknownOrigin = LocalCandidate([evidence]) with { Origin = "none" };

        var superseded = LocalCandidateSuppressor.SelectSupersededIds([ai], [ai, otherAi, unknownOrigin]);

        Assert.Empty(superseded);
    }

    [Fact]
    public void No_ai_candidates_means_nothing_is_superseded()
    {
        var local = LocalCandidate([Guid.NewGuid()]);

        var superseded = LocalCandidateSuppressor.SelectSupersededIds([], [local]);

        Assert.Empty(superseded);
    }

    private static ReportCandidate AiCandidate(IReadOnlyList<Guid> evidence) =>
        new(
            Guid.NewGuid(),
            new DateOnly(2026, 7, 27),
            new DateOnly(2026, 7, 28),
            "AI work item",
            "AI activity",
            "",
            "pending",
            .8,
            true,
            false,
            evidence,
            Origin: CandidateOrigins.Ai);

    private static ReportCandidate LocalCandidate(IReadOnlyList<Guid> evidence) =>
        new(
            Guid.NewGuid(),
            new DateOnly(2026, 7, 27),
            new DateOnly(2026, 7, 28),
            "手動メモ",
            "raw memo text",
            "",
            "pending",
            .8,
            true,
            false,
            evidence,
            Origin: CandidateOrigins.Local);
}
