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
}
