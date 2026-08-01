using WorkLogAI.Core;

namespace WorkLogAI.Tests;

public sealed class VaultSyncResultTests
{
    [Fact]
    public void Failure_sets_error_and_zeroes_everything_else()
    {
        var result = VaultSyncResult.Failure("デイリーノート出力フォルダが未設定です。");

        Assert.False(result.Succeeded);
        Assert.Equal("デイリーノート出力フォルダが未設定です。", result.Error);
        Assert.Equal(0, result.WrittenDays);
        Assert.Equal(0, result.ExtractedBatches);
        Assert.Empty(result.ExtractionErrors);
        Assert.Equal(0, result.TotalEntities);
        Assert.Equal(0, result.LinkedTargets);
    }

    [Fact]
    public void Succeeded_is_true_when_error_is_null()
    {
        var result = new VaultSyncResult(3, 1, [], 10, 4);

        Assert.True(result.Succeeded);
    }
}

public sealed class EntityExtractionBatcherTests
{
    [Fact]
    public void Empty_input_produces_no_batches()
    {
        var batches = EntityExtractionBatcher.Batch([], 100, 1024);

        Assert.Empty(batches);
    }

    [Fact]
    public void Blank_and_null_texts_are_skipped()
    {
        var batches = EntityExtractionBatcher.Batch(["", "  ", "実際のテキスト"], 100, 1024);

        var batch = Assert.Single(batches);
        Assert.Equal(["  ", "実際のテキスト"], batch);
    }

    [Fact]
    public void Splits_into_a_new_batch_once_the_count_cap_is_reached()
    {
        var texts = Enumerable.Range(0, 5).Select(i => $"text{i}").ToArray();

        var batches = EntityExtractionBatcher.Batch(texts, 2, 1024 * 1024);

        Assert.Equal(3, batches.Count);
        Assert.Equal(["text0", "text1"], batches[0]);
        Assert.Equal(["text2", "text3"], batches[1]);
        Assert.Equal(["text4"], batches[2]);
    }

    [Fact]
    public void Splits_into_a_new_batch_once_adding_a_text_would_exceed_the_byte_budget()
    {
        var texts = new[] { "12345", "12345", "12345" }; // 5 bytes each (ASCII)

        var batches = EntityExtractionBatcher.Batch(texts, 100, 12);

        Assert.Equal(2, batches.Count);
        Assert.Equal(["12345", "12345"], batches[0]);
        Assert.Equal(["12345"], batches[1]);
    }

    [Fact]
    public void A_single_text_larger_than_the_byte_budget_still_becomes_its_own_batch()
    {
        var oversized = new string('あ', 100); // multi-byte UTF-8, well over a tiny budget
        var texts = new[] { oversized, "short" };

        var batches = EntityExtractionBatcher.Batch(texts, 100, 10);

        Assert.Equal(2, batches.Count);
        Assert.Equal([oversized], batches[0]);
        Assert.Equal(["short"], batches[1]);
    }
}

public sealed class VaultExtractionTextGathererTests
{
    [Fact]
    public void Gathers_non_blank_quick_note_text_plus_meeting_title_and_summary_line()
    {
        var notes = new[]
        {
            new QuickNote(Guid.NewGuid(), DateTimeOffset.Now, "ACMEに連絡"),
            new QuickNote(Guid.NewGuid(), DateTimeOffset.Now, "現地調査")
        };
        var session = new MeetingSession(
            Guid.NewGuid(), "定例会議", "田中", MeetingKind.Meeting,
            DateTimeOffset.Now, DateTimeOffset.Now, MeetingStatus.Formatted, DateTimeOffset.Now);
        var summary = new MeetingSummary(Guid.NewGuid(), session.Id, "{}", "議事の要約", DateTimeOffset.Now);

        var texts = VaultExtractionTextGatherer.Gather(notes, [(session, summary)]);

        Assert.Equal(["ACMEに連絡", "現地調査", "定例会議", "議事の要約"], texts);
    }

    [Fact]
    public void Blank_session_title_or_summary_line_is_skipped()
    {
        var session = new MeetingSession(
            Guid.NewGuid(), "", "田中", MeetingKind.Meeting,
            DateTimeOffset.Now, DateTimeOffset.Now, MeetingStatus.Formatted, DateTimeOffset.Now);
        var summary = new MeetingSummary(Guid.NewGuid(), session.Id, "{}", "  ", DateTimeOffset.Now);

        var texts = VaultExtractionTextGatherer.Gather([], [(session, summary)]);

        Assert.Empty(texts);
    }

    [Fact]
    public void Everything_empty_returns_an_empty_list()
    {
        var texts = VaultExtractionTextGatherer.Gather([], []);

        Assert.Empty(texts);
    }
}

public sealed class MeetingMarkdownLinkerTests
{
    private const string Markdown =
        "---\n" +
        "date: 2026-07-31\n" +
        "type: meeting\n" +
        "participants: [ACME Corp, 田中]\n" +
        "tags: [worklog, meeting]\n" +
        "---\n" +
        "\n" +
        "# ACME Corp定例会議\n" +
        "\n" +
        "## 概要\n" +
        "ACME Corpの現地調査を実施した。\n";

    [Fact]
    public void Links_only_the_body_leaving_front_matter_untouched()
    {
        var result = MeetingMarkdownLinker.LinkBody(Markdown, text => text.Replace("ACME Corp", "[[ACME Corp]]"));

        Assert.Contains("participants: [ACME Corp, 田中]", result);
        Assert.Contains("# [[ACME Corp]]定例会議", result);
        Assert.Contains("[[ACME Corp]]の現地調査を実施した。", result);
    }

    [Fact]
    public void Reconstructs_the_original_text_exactly_when_the_transform_is_the_identity()
    {
        var result = MeetingMarkdownLinker.LinkBody(Markdown, text => text);

        Assert.Equal(Markdown, result);
    }

    [Fact]
    public void Text_with_no_front_matter_is_linked_in_full()
    {
        var result = MeetingMarkdownLinker.LinkBody("ACME Corpに連絡した", text => text.Replace("ACME Corp", "[[ACME Corp]]"));

        Assert.Equal("[[ACME Corp]]に連絡した", result);
    }

    [Fact]
    public void Malformed_front_matter_with_no_closing_delimiter_falls_back_to_linking_everything()
    {
        var malformed = "---\ndate: 2026-07-31\nACME Corpのメモ";

        var result = MeetingMarkdownLinker.LinkBody(malformed, text => text.Replace("ACME Corp", "[[ACME Corp]]"));

        Assert.Equal("---\ndate: 2026-07-31\n[[ACME Corp]]のメモ", result);
    }

    [Fact]
    public void Null_or_empty_input_returns_an_empty_string()
    {
        Assert.Equal(string.Empty, MeetingMarkdownLinker.LinkBody(null, text => text));
        Assert.Equal(string.Empty, MeetingMarkdownLinker.LinkBody(string.Empty, text => text));
    }
}
