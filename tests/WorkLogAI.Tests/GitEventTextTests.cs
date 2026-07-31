using WorkLogAI.Core;

namespace WorkLogAI.Tests;

public sealed class GitEventTextTests
{
    [Fact]
    public void StripFileList_removes_file_list_but_keeps_summary_and_statistics()
    {
        var body = "READMEを更新。変更ファイル: README.md, src/App.cs。統計: +12 / -3";

        var result = GitEventText.StripFileList(body);

        Assert.Equal("READMEを更新。統計: +12 / -3", result);
        Assert.DoesNotContain("変更ファイル", result);
        Assert.DoesNotContain("README.md", result);
    }

    [Fact]
    public void StripFileList_handles_file_list_and_statistics_with_no_summary()
    {
        var body = "変更ファイル: README.md, src/App.cs。統計: +12 / -3";

        var result = GitEventText.StripFileList(body);

        Assert.Equal("統計: +12 / -3", result);
    }

    [Fact]
    public void StripFileList_is_a_no_op_when_only_statistics_are_present()
    {
        var body = "統計: +12 / -3";

        var result = GitEventText.StripFileList(body);

        Assert.Equal("統計: +12 / -3", result);
    }

    [Fact]
    public void StripFileList_is_a_no_op_when_only_summary_and_statistics_are_present()
    {
        var body = "READMEを更新。統計: +12 / -3";

        var result = GitEventText.StripFileList(body);

        Assert.Equal("READMEを更新。統計: +12 / -3", result);
    }

    [Fact]
    public void StripFileList_removes_a_trailing_file_list_with_no_statistics_part()
    {
        // Matches LocalGitCollector's uncommitted-changes body, which has no
        // 統計 part at all.
        var body = "変更ファイル: README.md, src/App.cs";

        var result = GitEventText.StripFileList(body);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void StripFileList_returns_empty_string_for_empty_body()
    {
        Assert.Equal(string.Empty, GitEventText.StripFileList(string.Empty));
    }

    [Fact]
    public void StripFileList_never_leaves_leading_trailing_or_doubled_separators()
    {
        var body = "変更ファイル: only.txt";

        var result = GitEventText.StripFileList(body);

        Assert.DoesNotContain("。。", result);
        Assert.False(result.StartsWith('。'));
        Assert.False(result.EndsWith('。'));
    }

    [Fact]
    public void StripFileList_removes_every_segment_from_a_merged_row_with_two_file_lists()
    {
        // Shape produced by CandidateMergeService joining two pre-fix stored
        // candidate activities (each "Title — 変更ファイル: …。統計: …") with " / ".
        var body = "subjectA — 変更ファイル: a.md, b.md。統計: +1 / -0 / summaryB / subjectC — "
            + "変更ファイル: c.cs。統計: +2 / -1";

        var result = GitEventText.StripFileList(body);

        Assert.Equal(
            "subjectA — 統計: +1 / -0 / summaryB / subjectC — 統計: +2 / -1",
            result);
        Assert.DoesNotContain("変更ファイル", result);
        Assert.DoesNotContain("a.md", result);
        Assert.DoesNotContain("c.cs", result);
    }

    [Fact]
    public void StripFileList_drops_a_dangling_title_separator_when_a_merged_chunk_has_no_statistics()
    {
        // Second chunk mirrors LocalGitCollector's uncommitted-changes body (title
        // joined directly to the file list, no summary, no 統計 part) merged after
        // a normal commit chunk that does have statistics.
        var body = "件名A — 実装完了。変更ファイル: a.cs。統計: +5 / -1 / 未コミット変更 — "
            + "変更ファイル: b.txt, c.txt";

        var result = GitEventText.StripFileList(body);

        Assert.Equal("件名A — 実装完了。統計: +5 / -1 / 未コミット変更", result);
        Assert.DoesNotContain("変更ファイル", result);
        Assert.DoesNotContain("b.txt", result);
        Assert.False(result.EndsWith(" — ", StringComparison.Ordinal));
    }

    [Fact]
    public void StripFileList_removes_back_to_back_segments_with_no_content_between_them()
    {
        var body = "変更ファイル: a.md。変更ファイル: b.md。統計: +1 / -2";

        var result = GitEventText.StripFileList(body);

        Assert.Equal("統計: +1 / -2", result);
        Assert.DoesNotContain("変更ファイル", result);
    }

    [Fact]
    public void StripFileList_removes_a_trailing_segment_with_no_statistics_after_a_chunk_separator()
    {
        var body = "summaryA / 未コミット変更 — 変更ファイル: only.txt";

        var result = GitEventText.StripFileList(body);

        Assert.Equal("summaryA / 未コミット変更", result);
    }
}
