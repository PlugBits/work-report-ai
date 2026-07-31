using WorkLogAI.Core;

namespace WorkLogAI.Tests;

public sealed class CandidateBadgeTests
{
    [Fact]
    public void Resolve_returns_ai_badge_regardless_of_source_type()
    {
        var badge = CandidateBadge.Resolve(CandidateOrigins.Ai, SourceTypes.Git);

        Assert.Equal("AI", badge.Label);
        Assert.Equal("#2563EB", badge.HexColor);
    }

    [Fact]
    public void Resolve_returns_manual_badge_regardless_of_source_type()
    {
        var badge = CandidateBadge.Resolve(CandidateOrigins.Manual, null);

        Assert.Equal("手動追加", badge.Label);
        Assert.Equal("#0D9488", badge.HexColor);
    }

    [Theory]
    [InlineData(SourceTypes.Manual, "メモ", "#16A34A")]
    [InlineData(SourceTypes.Meeting, "議事録", "#7C3AED")]
    [InlineData(SourceTypes.Git, "Git", "#6B7280")]
    [InlineData(SourceTypes.Codex, "Codex", "#6B7280")]
    [InlineData(SourceTypes.File, "ファイル", "#D97706")]
    [InlineData(SourceTypes.OutlookMail, "メール", "#0284C7")]
    [InlineData(SourceTypes.Calendar, "予定", "#0284C7")]
    public void Resolve_maps_local_origin_by_backing_source_type(
        string sourceType, string expectedLabel, string expectedColor)
    {
        var badge = CandidateBadge.Resolve(CandidateOrigins.Local, sourceType);

        Assert.Equal(expectedLabel, badge.Label);
        Assert.Equal(expectedColor, badge.HexColor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public void Resolve_falls_back_to_local_badge_for_missing_or_unrecognized_source_type(string? sourceType)
    {
        var badge = CandidateBadge.Resolve(CandidateOrigins.Local, sourceType);

        Assert.Equal("ローカル", badge.Label);
        Assert.Equal("#6B7280", badge.HexColor);
    }
}
