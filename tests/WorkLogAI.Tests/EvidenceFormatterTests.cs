using WorkLogAI.Core;
using WorkLogAI.Infrastructure;

namespace WorkLogAI.Tests;

public sealed class EvidenceFormatterTests
{
    private static readonly DateTimeOffset Occurred = new(2026, 7, 28, 9, 30, 0, TimeSpan.FromHours(9));

    private static SourceEvent Make(string sourceType, string title, string body, string evidence) =>
        new(
            Guid.NewGuid(),
            Occurred,
            sourceType,
            title,
            body,
            evidence,
            "some-ref",
            1.0,
            "some-hash",
            Occurred);

    [Fact]
    public void Manual_events_show_the_memo_body_instead_of_a_generic_line()
    {
        var source = Make(SourceTypes.Manual, "手動メモ", "顧客Aへ見積もりを送付した。", "ユーザーがクイック入力で記録");

        var result = EvidenceFormatter.Describe(source);

        Assert.Contains("[手動メモ]", result);
        Assert.Contains("顧客Aへ見積もりを送付した。", result);
        Assert.DoesNotContain("ユーザーがクイック入力で記録", result);
    }

    [Fact]
    public void Meeting_events_show_the_summary_body()
    {
        var source = Make(SourceTypes.Meeting, "定例会議", "決定事項: リリース日は8/5。", "会議議事録");

        var result = EvidenceFormatter.Describe(source);

        Assert.Contains("[議事録]", result);
        Assert.Contains("決定事項: リリース日は8/5。", result);
    }

    [Fact]
    public void Git_events_fall_back_to_title_and_evidence_like_before()
    {
        var source = Make(SourceTypes.Git, "feat: add export", "3 files changed", "commit abc123");

        var result = EvidenceFormatter.Describe(source);

        Assert.Contains("[Git]", result);
        Assert.Contains("feat: add export — commit abc123", result);
    }

    [Theory]
    [InlineData(SourceTypes.Manual, "手動メモ")]
    [InlineData(SourceTypes.Git, "Git")]
    [InlineData(SourceTypes.Codex, "Codex")]
    [InlineData(SourceTypes.File, "更新ファイル")]
    [InlineData(SourceTypes.OutlookMail, "メール")]
    [InlineData(SourceTypes.Calendar, "予定")]
    [InlineData(SourceTypes.Meeting, "議事録")]
    [InlineData("some_future_source", "some_future_source")]
    public void Source_type_maps_to_the_expected_japanese_label(string sourceType, string expectedLabel)
    {
        var source = Make(sourceType, "title", "body", "evidence");

        var result = EvidenceFormatter.Describe(source);

        Assert.Contains($"[{expectedLabel}]", result);
    }

    [Fact]
    public void Manual_snippet_is_bounded_to_roughly_two_hundred_characters()
    {
        var longBody = new string('a', 500);
        var source = Make(SourceTypes.Manual, "title", longBody, "evidence");

        var result = EvidenceFormatter.Describe(source);
        var snippetStart = result.IndexOf(']') + 2;
        var snippet = result[snippetStart..];

        Assert.Equal(200, snippet.Length);
    }

    [Fact]
    public void Non_manual_snippet_keeps_the_original_title_and_evidence_bounds()
    {
        // Kept under SafeTextSanitizer's internal 500-char per-line cap so this
        // isolates EvidenceFormatter's own 300/800 bounds (matching the previous
        // CandidateWindow.DescribeEvidence behavior) from that unrelated cap.
        var overTitleLimit = new string('t', 350);
        var underEvidenceLimit = new string('e', 400);
        var source = Make(SourceTypes.File, overTitleLimit, "unused body", underEvidenceLimit);

        var result = EvidenceFormatter.Describe(source);

        Assert.Contains(new string('t', 300) + " — " + underEvidenceLimit, result);
        Assert.DoesNotContain(new string('t', 301), result);
    }

    [Fact]
    public void Unknown_source_event_id_renders_the_unchanged_fallback_line()
    {
        var missingId = Guid.NewGuid();

        var result = EvidenceFormatter.Describe([missingId], new Dictionary<Guid, SourceEvent>());

        Assert.Equal($"[{missingId:D}] 根拠詳細なし", result);
    }

    [Fact]
    public void List_overload_joins_multiple_events_with_newlines_and_preserves_order()
    {
        var manual = Make(SourceTypes.Manual, "t1", "body1", "e1");
        var git = Make(SourceTypes.Git, "t2", "body2", "e2");
        var events = new Dictionary<Guid, SourceEvent> { [manual.Id] = manual, [git.Id] = git };

        var result = EvidenceFormatter.Describe([manual.Id, git.Id], events);

        var lines = result.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Contains("[手動メモ]", lines[0]);
        Assert.Contains("[Git]", lines[1]);
    }
}
