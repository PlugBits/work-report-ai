using WorkLogAI.Core;

namespace WorkLogAI.Tests;

public sealed class DailyNoteBuilderTests
{
    private static readonly DateOnly Date = new(2026, 7, 31); // Friday

    [Fact]
    public void Everything_empty_returns_null_so_the_caller_can_skip_writing_a_file()
    {
        var result = DailyNoteBuilder.Build(Date, [], [], [], [], text => text);

        Assert.Null(result);
    }

    [Fact]
    public void Frontmatter_and_heading_include_the_date_and_japanese_weekday()
    {
        var notes = new[] { Note("09:15", "現地調査") };

        var result = DailyNoteBuilder.Build(Date, notes, [], [], [], text => text);

        Assert.StartsWith(
            "---\ndate: 2026-07-31\ntags: [worklog, daily]\n---\n\n# 2026-07-31 (金)\n",
            result);
    }

    [Fact]
    public void Memo_section_lists_notes_in_ascending_time_order_and_applies_the_link_transform()
    {
        var notes = new[]
        {
            Note("16:00", "午後の作業"),
            Note("09:00", "ACMEに連絡")
        };

        var result = DailyNoteBuilder.Build(Date, notes, [], [], [], text => text.Replace("ACME", "[[ACME Corp|ACME]]"));

        Assert.Contains(
            "## メモ\n- 09:00 [[ACME Corp|ACME]]に連絡\n- 16:00 午後の作業\n",
            result);
    }

    [Fact]
    public void Meeting_section_links_directly_to_the_exported_file_name()
    {
        var meetings = new[] { new DailyNoteMeeting("定例会議", "2026-07-31_定例会議") };

        var result = DailyNoteBuilder.Build(Date, [], meetings, [], [], text => text);

        Assert.Contains("## 会議\n- [[2026-07-31_定例会議]]\n", result);
    }

    [Fact]
    public void Development_section_groups_git_events_by_repository_last_path_segment()
    {
        var gitEvents = new[]
        {
            GitEvent("feat: A追加", @"git:C:\repos\worklogai:abc123"),
            GitEvent("fix: Bバグ修正", "git:/home/user/worklogai:def456"),
            GitEvent("feat: C追加", "git:/home/user/other-repo:ghi789")
        };

        var result = DailyNoteBuilder.Build(Date, [], [], gitEvents, [], text => text);

        Assert.Contains(
            "## 開発\n- worklogai: 2 commits — feat: A追加…\n- other-repo: 1 commits — feat: C追加…\n",
            result);
    }

    [Fact]
    public void Weekly_report_section_lists_work_item_and_linked_activity_without_a_circled_number()
    {
        var candidateLines = new[] { ("案件X", "ACMEの現地調査を実施") };

        var result = DailyNoteBuilder.Build(
            Date, [], [], [], candidateLines, text => text.Replace("ACME", "[[ACME Corp|ACME]]"));

        Assert.Contains("## 週報\n- 案件X: [[ACME Corp|ACME]]の現地調査を実施\n", result);
    }

    [Fact]
    public void Empty_sections_are_omitted_entirely()
    {
        var result = DailyNoteBuilder.Build(Date, [Note("09:00", "メモのみ")], [], [], [], text => text);

        Assert.DoesNotContain("## 会議", result);
        Assert.DoesNotContain("## 開発", result);
        Assert.DoesNotContain("## 週報", result);
    }

    [Fact]
    public void All_sections_render_together_in_a_fixed_order()
    {
        var notes = new[] { Note("09:00", "メモ") };
        var meetings = new[] { new DailyNoteMeeting("会議名", "2026-07-31_会議名") };
        var gitEvents = new[] { GitEvent("feat: 追加", "git:/repo:hash") };
        var candidateLines = new[] { ("案件", "活動内容") };

        var result = DailyNoteBuilder.Build(Date, notes, meetings, gitEvents, candidateLines, text => text)!;

        var memoIndex = result.IndexOf("## メモ", StringComparison.Ordinal);
        var meetingIndex = result.IndexOf("## 会議", StringComparison.Ordinal);
        var devIndex = result.IndexOf("## 開発", StringComparison.Ordinal);
        var reportIndex = result.IndexOf("## 週報", StringComparison.Ordinal);
        Assert.True(memoIndex < meetingIndex);
        Assert.True(meetingIndex < devIndex);
        Assert.True(devIndex < reportIndex);
    }

    private static QuickNote Note(string time, string text)
    {
        var parts = time.Split(':');
        var createdAt = new DateTimeOffset(
            Date.Year, Date.Month, Date.Day, int.Parse(parts[0]), int.Parse(parts[1]), 0, TimeSpan.FromHours(9));
        return new QuickNote(Guid.NewGuid(), createdAt, text);
    }

    private static SourceEvent GitEvent(string title, string sourceRef) => SourceEventFactory.Create(
        new DateTimeOffset(Date, TimeOnly.MinValue, TimeSpan.FromHours(9)),
        SourceTypes.Git,
        title,
        "body",
        "evidence",
        sourceRef,
        0.9);
}
