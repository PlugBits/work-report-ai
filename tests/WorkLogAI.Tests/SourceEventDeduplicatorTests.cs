using WorkLogAI.Core;

namespace WorkLogAI.Tests;

public sealed class SourceEventDeduplicatorTests
{
    [Fact]
    public void Two_events_with_the_same_ref_keep_only_the_one_collected_last()
    {
        var older = Event("meeting:stable-ref", "v1", collectedAt: 1);
        var newer = Event("meeting:stable-ref", "v2", collectedAt: 2);

        var result = SourceEventDeduplicator.LatestPerRef([older, newer]);

        Assert.Equal(newer.Id, Assert.Single(result).Id);
    }

    [Fact]
    public void Events_with_different_refs_are_all_kept()
    {
        var first = Event("ref:1", "v1", collectedAt: 1);
        var second = Event("ref:2", "v1", collectedAt: 1);

        var result = SourceEventDeduplicator.LatestPerRef([first, second]);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, item => item.Id == first.Id);
        Assert.Contains(result, item => item.Id == second.Id);
    }

    [Fact]
    public void Blank_refs_pass_through_untouched_even_when_duplicated()
    {
        var first = Event("", "blank-a", collectedAt: 1);
        var second = Event("   ", "blank-b", collectedAt: 1);

        var result = SourceEventDeduplicator.LatestPerRef([first, second]);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, item => item.Id == first.Id);
        Assert.Contains(result, item => item.Id == second.Id);
    }

    [Fact]
    public void Tie_break_falls_back_to_id_when_collected_at_and_occurred_at_match()
    {
        var occurredAtSame = new DateTimeOffset(2026, 7, 28, 9, 0, 0, TimeSpan.Zero);
        var collectedAtSame = new DateTimeOffset(2026, 7, 28, 10, 0, 0, TimeSpan.Zero);

        // Same CollectedAt and OccurredAt, only the content (and so the Id) differs:
        // the higher Id (ordinal "D" form) must win, regardless of input order.
        var a = SourceEventFactory.Create(
            occurredAtSame, SourceTypes.Meeting, "a", "body-a", "evidence", "ref:tie", 1, collectedAtSame);
        var b = SourceEventFactory.Create(
            occurredAtSame, SourceTypes.Meeting, "b", "body-b", "evidence", "ref:tie", 1, collectedAtSame);
        var expectedWinnerId = string.CompareOrdinal(a.Id.ToString("D"), b.Id.ToString("D")) > 0 ? a.Id : b.Id;

        var resultAb = SourceEventDeduplicator.LatestPerRef([a, b]);
        var resultBa = SourceEventDeduplicator.LatestPerRef([b, a]);

        Assert.Equal(expectedWinnerId, Assert.Single(resultAb).Id);
        Assert.Equal(expectedWinnerId, Assert.Single(resultBa).Id);
    }

    [Fact]
    public void Occurred_at_breaks_ties_when_collected_at_matches()
    {
        var collectedAtSame = new DateTimeOffset(2026, 7, 28, 10, 0, 0, TimeSpan.Zero);
        var older = SourceEventFactory.Create(
            new DateTimeOffset(2026, 7, 27, 9, 0, 0, TimeSpan.Zero),
            SourceTypes.Meeting, "a", "body-a", "evidence", "ref:tie2", 1, collectedAtSame);
        var newer = SourceEventFactory.Create(
            new DateTimeOffset(2026, 7, 28, 9, 0, 0, TimeSpan.Zero),
            SourceTypes.Meeting, "b", "body-b", "evidence", "ref:tie2", 1, collectedAtSame);

        var result = SourceEventDeduplicator.LatestPerRef([older, newer]);

        Assert.Equal(newer.Id, Assert.Single(result).Id);
    }

    private static SourceEvent Event(string sourceRef, string bodySeed, int collectedAt) =>
        SourceEventFactory.Create(
            new DateTimeOffset(2026, 7, 28, 9, 0, 0, TimeSpan.Zero),
            SourceTypes.Meeting,
            "title",
            $"body-{bodySeed}",
            "evidence",
            sourceRef,
            1,
            new DateTimeOffset(2026, 7, 28, 9, 0, 0, TimeSpan.Zero).AddHours(collectedAt));
}
