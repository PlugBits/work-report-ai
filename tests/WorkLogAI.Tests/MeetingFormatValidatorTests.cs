using WorkLogAI.Core;

namespace WorkLogAI.Tests;

public sealed class MeetingFormatValidatorTests
{
    [Fact]
    public void Valid_envelope_converts_to_a_formatted_result()
    {
        var envelope = new MeetingFormatEnvelope
        {
            SummaryLine = "定例会議で来期予算を承認し、資料送付を宿題とした。",
            Overview = "予算について議論した。",
            Decisions = [new MeetingFormatDecisionPayload { Text = "来期予算を承認" }],
            ActionItems = [new MeetingFormatActionItemPayload { Text = "資料を送付", Owner = "田中", Due = "2026-08-05" }],
            Topics = [new MeetingFormatTopicPayload { Title = "予算", Detail = "来期の予算配分について" }]
        };

        var result = MeetingFormatValidator.Validate(envelope);

        Assert.True(result.Succeeded);
        Assert.Equal(envelope.SummaryLine, result.Formatted!.SummaryLine);
        Assert.Equal("来期予算を承認", Assert.Single(result.Formatted.Decisions));
        var actionItem = Assert.Single(result.Formatted.ActionItems);
        Assert.Equal("資料を送付", actionItem.Text);
        Assert.Equal("田中", actionItem.Owner);
        Assert.Equal("2026-08-05", actionItem.Due);
        var topic = Assert.Single(result.Formatted.Topics);
        Assert.Equal("予算", topic.Title);
    }

    [Fact]
    public void Null_envelope_fails_with_a_japanese_error()
    {
        var result = MeetingFormatValidator.Validate(null);

        Assert.False(result.Succeeded);
        Assert.Null(result.Formatted);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_summary_line_fails(string summaryLine)
    {
        var envelope = Envelope(summaryLine: summaryLine);

        var result = MeetingFormatValidator.Validate(envelope);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Summary_line_over_120_characters_fails()
    {
        var envelope = Envelope(summaryLine: new string('あ', 121));

        var result = MeetingFormatValidator.Validate(envelope);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Summary_line_of_exactly_120_characters_succeeds()
    {
        var envelope = Envelope(summaryLine: new string('あ', 120));

        var result = MeetingFormatValidator.Validate(envelope);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Blank_decision_text_fails()
    {
        var envelope = Envelope(decisions: [new MeetingFormatDecisionPayload { Text = "  " }]);

        var result = MeetingFormatValidator.Validate(envelope);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Blank_action_item_text_fails()
    {
        var envelope = Envelope(actionItems:
            [new MeetingFormatActionItemPayload { Text = " ", Owner = null, Due = null }]);

        var result = MeetingFormatValidator.Validate(envelope);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Blank_topic_title_fails()
    {
        var envelope = Envelope(topics: [new MeetingFormatTopicPayload { Title = "", Detail = "detail" }]);

        var result = MeetingFormatValidator.Validate(envelope);

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("2026/08/05")]
    [InlineData("08-05-2026")]
    [InlineData("not-a-date")]
    [InlineData("2026-13-01")]
    public void Malformed_due_date_fails(string due)
    {
        var envelope = Envelope(actionItems:
            [new MeetingFormatActionItemPayload { Text = "宿題", Owner = null, Due = due }]);

        var result = MeetingFormatValidator.Validate(envelope);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Null_due_is_accepted()
    {
        var envelope = Envelope(actionItems:
            [new MeetingFormatActionItemPayload { Text = "宿題", Owner = null, Due = null }]);

        var result = MeetingFormatValidator.Validate(envelope);

        Assert.True(result.Succeeded);
        Assert.Null(Assert.Single(result.Formatted!.ActionItems).Due);
    }

    private static MeetingFormatEnvelope Envelope(
        string summaryLine = "要約",
        List<MeetingFormatDecisionPayload>? decisions = null,
        List<MeetingFormatActionItemPayload>? actionItems = null,
        List<MeetingFormatTopicPayload>? topics = null) => new()
    {
        SummaryLine = summaryLine,
        Overview = "overview",
        Decisions = decisions ?? [],
        ActionItems = actionItems ?? [],
        Topics = topics ?? []
    };
}
