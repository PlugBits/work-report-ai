using WorkLogAI.Core;

namespace WorkLogAI.Tests;

public sealed class CoreTests
{
    [Fact]
    public void QuickNote_rejects_blank_text()
    {
        Assert.Throws<ArgumentException>(() =>
            new QuickNote(Guid.NewGuid(), DateTimeOffset.UtcNow, "   "));
    }

    [Theory]
    [InlineData(DayOfWeek.Monday, "2026-07-27", "2026-08-02")]
    [InlineData(DayOfWeek.Sunday, "2026-07-26", "2026-08-01")]
    [InlineData(DayOfWeek.Thursday, "2026-07-30", "2026-08-05")]
    public void WeekRange_honors_configurable_start_and_includes_boundaries(
        DayOfWeek weekStartsOn,
        string expectedStart,
        string expectedEnd)
    {
        var range = new WeekRangeCalculator(weekStartsOn)
            .GetWeekRange(new DateOnly(2026, 7, 30));

        Assert.Equal(DateOnly.Parse(expectedStart), range.Start);
        Assert.Equal(DateOnly.Parse(expectedEnd), range.End);
        Assert.True(range.Contains(range.Start));
        Assert.True(range.Contains(range.End));
        Assert.False(range.Contains(range.Start.AddDays(-1)));
        Assert.False(range.Contains(range.End.AddDays(1)));
    }

    [Fact]
    public void Manual_mapping_is_deterministic_and_does_not_invent_a_result()
    {
        var note = new QuickNote(
            Guid.Parse("4bca44b5-10c1-4b93-9c15-520b08ec53c7"),
            new DateTimeOffset(2026, 7, 30, 14, 30, 0, TimeSpan.FromHours(-4)),
            "顧客A初品5点 検査 全数合格 出荷指示");

        var row = new ManualReportMapper().Map(note);

        Assert.Equal(new DateOnly(2026, 7, 30), row.Date);
        Assert.Equal("手動メモ", row.WorkItem);
        Assert.Equal(note.Text, row.Activity);
        Assert.Equal(string.Empty, row.ResultOrNext);
        Assert.Equal(ReportCategories.Internal, row.Category);
    }

    [Theory]
    [InlineData("openai.api-key")]
    [InlineData("github_token")]
    [InlineData("password")]
    [InlineData("client-secret")]
    [InlineData("OpenAIApiKey")]
    public void Settings_policy_rejects_secret_keys(string key)
    {
        Assert.Throws<InvalidOperationException>(() => SettingKeyPolicy.EnsureNonSecret(key));
    }

    [Fact]
    public void Settings_policy_allows_non_secret_profile_values()
    {
        SettingKeyPolicy.EnsureNonSecret(AppSettingKeys.CompanyName);
        SettingKeyPolicy.EnsureNonSecret(AppSettingKeys.WeekStartsOn);
    }
}
