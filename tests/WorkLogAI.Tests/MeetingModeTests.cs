using WorkLogAI.Core;

namespace WorkLogAI.Tests;

public sealed class MeetingLineParserTests
{
    [Theory]
    [InlineData("@buy milk", MeetingMarker.Todo, "buy milk")]
    [InlineData("＠買い物", MeetingMarker.Todo, "買い物")]
    [InlineData("!approved the budget", MeetingMarker.Decision, "approved the budget")]
    [InlineData("！予算を承認", MeetingMarker.Decision, "予算を承認")]
    [InlineData("plain note", MeetingMarker.None, "plain note")]
    [InlineData("  @  trimmed inside  ", MeetingMarker.Todo, "trimmed inside")]
    public void Parses_marker_and_text(string raw, MeetingMarker expectedMarker, string expectedText)
    {
        var result = MeetingLineParser.Parse(raw);

        Assert.NotNull(result);
        Assert.Equal(expectedMarker, result!.Value.Marker);
        Assert.Equal(expectedText, result.Value.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("@")]
    [InlineData("@   ")]
    [InlineData("!")]
    [InlineData("＠")]
    [InlineData("！ ")]
    [InlineData(null)]
    public void Rejects_input_with_no_meaningful_text(string? raw)
    {
        Assert.Null(MeetingLineParser.Parse(raw));
    }

    [Fact]
    public void Leading_and_trailing_whitespace_around_the_whole_line_is_trimmed()
    {
        var result = MeetingLineParser.Parse("   plain note   ");

        Assert.NotNull(result);
        Assert.Equal(MeetingMarker.None, result!.Value.Marker);
        Assert.Equal("plain note", result.Value.Text);
    }
}

public sealed class MeetingParticipantsFormatterTests
{
    [Theory]
    [InlineData("田中、鈴木", new[] { "田中", "鈴木" })]
    [InlineData("田中,鈴木", new[] { "田中", "鈴木" })]
    [InlineData("田中 鈴木　佐藤", new[] { "田中", "鈴木", "佐藤" })]
    [InlineData("  田中 、 鈴木  ", new[] { "田中", "鈴木" })]
    [InlineData("", new string[0])]
    [InlineData(null, new string[0])]
    public void Splits_on_common_japanese_and_ascii_separators(string? participants, string[] expected)
    {
        Assert.Equal(expected, MeetingParticipantsFormatter.Split(participants));
    }

    [Fact]
    public void Formats_empty_list_as_empty_yaml_array()
    {
        Assert.Equal("[]", MeetingParticipantsFormatter.ToYamlInlineList([]));
    }

    [Fact]
    public void Formats_plain_names_as_inline_yaml_list()
    {
        Assert.Equal("[田中, 鈴木]", MeetingParticipantsFormatter.ToYamlInlineList(["田中", "鈴木"]));
    }

    [Fact]
    public void Quotes_names_containing_yaml_special_characters()
    {
        Assert.Equal("[\"a: b\", c]", MeetingParticipantsFormatter.ToYamlInlineList(["a: b", "c"]));
    }
}

public sealed class MeetingMarkdownBuilderTests
{
    private static MeetingSession Session(
        string title = "定例会議",
        string participants = "田中、鈴木",
        MeetingKind kind = MeetingKind.Meeting) => new(
        Guid.NewGuid(),
        title,
        participants,
        kind,
        new DateTimeOffset(2026, 7, 31, 10, 0, 0, TimeSpan.FromHours(9)),
        null,
        MeetingStatus.Closed,
        new DateTimeOffset(2026, 7, 31, 10, 0, 0, TimeSpan.FromHours(9)));

    private static MeetingLine Line(int lineNo, MeetingMarker marker, string text, int hour, int minute) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        lineNo,
        marker,
        text,
        new DateTimeOffset(2026, 7, 31, hour, minute, 0, TimeSpan.FromHours(9)));

    [Fact]
    public void Front_matter_includes_date_type_participants_and_fixed_tags()
    {
        var markdown = MeetingMarkdownBuilder.Build(Session(), [], null, includeRawLog: true);

        Assert.StartsWith(
            "---\ndate: 2026-07-31\ntype: meeting\nparticipants: [田中, 鈴木]\ntags: [worklog, meeting]\n---\n\n",
            markdown);
    }

    [Fact]
    public void Title_heading_follows_front_matter()
    {
        var markdown = MeetingMarkdownBuilder.Build(Session(title: "予算会議"), [], null, includeRawLog: true);

        Assert.Contains("# 予算会議\n", markdown);
    }

    [Fact]
    public void Null_formatted_result_omits_summary_sections_and_emits_only_raw_log()
    {
        var lines = new[] { Line(1, MeetingMarker.None, "hello", 10, 5) };

        var markdown = MeetingMarkdownBuilder.Build(Session(), lines, null, includeRawLog: true);

        Assert.DoesNotContain("## 概要", markdown);
        Assert.DoesNotContain("## 決定事項", markdown);
        Assert.DoesNotContain("## 宿題", markdown);
        Assert.DoesNotContain("## 論点", markdown);
        Assert.Contains("## 生ログ", markdown);
        Assert.Contains("- 10:05 hello", markdown);
    }

    [Fact]
    public void Include_raw_log_false_omits_the_raw_log_section_entirely()
    {
        var lines = new[] { Line(1, MeetingMarker.None, "hello", 10, 5) };

        var markdown = MeetingMarkdownBuilder.Build(Session(), lines, null, includeRawLog: false);

        Assert.DoesNotContain("## 生ログ", markdown);
    }

    [Fact]
    public void Raw_log_restores_marker_prefixes()
    {
        var lines = new[]
        {
            Line(1, MeetingMarker.None, "plain note", 9, 0),
            Line(2, MeetingMarker.Todo, "buy milk", 9, 5),
            Line(3, MeetingMarker.Decision, "approved", 9, 10)
        };

        var markdown = MeetingMarkdownBuilder.Build(Session(), lines, null, includeRawLog: true);

        Assert.Contains("- 09:00 plain note", markdown);
        Assert.Contains("- 09:05 @buy milk", markdown);
        Assert.Contains("- 09:10 !approved", markdown);
    }

    [Fact]
    public void Formatted_result_emits_all_sections_in_order()
    {
        var formatted = new MeetingFormattedResult(
            "定例会議の要約",
            "予算について議論した。",
            ["来期予算を承認"],
            [new MeetingActionItem("資料を送付", "田中", "8/5")],
            [new MeetingTopic("予算", "来期の予算配分について")]);

        var markdown = MeetingMarkdownBuilder.Build(Session(), [], formatted, includeRawLog: true);

        var overviewIndex = markdown.IndexOf("## 概要", StringComparison.Ordinal);
        var decisionsIndex = markdown.IndexOf("## 決定事項", StringComparison.Ordinal);
        var actionItemsIndex = markdown.IndexOf("## 宿題", StringComparison.Ordinal);
        var topicsIndex = markdown.IndexOf("## 論点", StringComparison.Ordinal);
        var rawLogIndex = markdown.IndexOf("## 生ログ", StringComparison.Ordinal);

        Assert.True(overviewIndex < decisionsIndex);
        Assert.True(decisionsIndex < actionItemsIndex);
        Assert.True(actionItemsIndex < topicsIndex);
        Assert.True(topicsIndex < rawLogIndex);
        Assert.Contains("予算について議論した。", markdown);
        Assert.Contains("- 来期予算を承認", markdown);
        Assert.Contains("- [ ] 資料を送付（担当: 田中 / 期限: 8/5）", markdown);
        Assert.Contains("### 予算", markdown);
        Assert.Contains("来期の予算配分について", markdown);
    }

    [Theory]
    [InlineData("担当だけ", "田中", null, "- [ ] 担当だけ（担当: 田中）")]
    [InlineData("期限だけ", null, "8/5", "- [ ] 期限だけ（期限: 8/5）")]
    [InlineData("両方なし", null, null, "- [ ] 両方なし")]
    public void Action_item_suffix_omits_missing_owner_or_due(
        string text,
        string? owner,
        string? due,
        string expectedLine)
    {
        var formatted = new MeetingFormattedResult(
            "summary",
            "overview",
            [],
            [new MeetingActionItem(text, owner, due)],
            []);

        var markdown = MeetingMarkdownBuilder.Build(Session(), [], formatted, includeRawLog: false);

        Assert.Contains(expectedLine, markdown);
    }

    [Fact]
    public void Empty_formatted_collections_render_a_placeholder_instead_of_blank_sections()
    {
        var formatted = new MeetingFormattedResult("summary", "", [], [], []);

        var markdown = MeetingMarkdownBuilder.Build(Session(), [], formatted, includeRawLog: false);

        Assert.Contains("## 概要\n（記載なし）", markdown);
        Assert.Contains("## 決定事項\n- （記載なし）", markdown);
        Assert.Contains("## 宿題\n- （記載なし）", markdown);
        Assert.Contains("## 論点\n（記載なし）", markdown);
    }
}
