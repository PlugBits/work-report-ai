using WorkLogAI.Infrastructure;

namespace WorkLogAI.Tests;

public sealed class ErrorLogTests
{
    [Fact]
    public void FormatLine_includes_timestamp_context_and_message()
    {
        var line = ErrorLog.FormatLine(new DateTime(2026, 7, 30, 9, 5, 3), "App.Startup", "boom");

        Assert.Equal("2026-07-30 09:05:03 [App.Startup] boom", line);
    }

    [Fact]
    public void SelectExpiredLogFiles_keeps_recent_files_and_expires_old_ones()
    {
        var today = new DateOnly(2026, 7, 30);
        var files = new[]
        {
            "worklog-202607.log",
            "worklog-202606.log",
            "worklog-202605.log",
            "worklog-202604.log"
        };

        var expired = ErrorLog.SelectExpiredLogFiles(files, today);

        Assert.Equal(new[] { "worklog-202604.log" }, expired);
    }

    [Fact]
    public void SelectExpiredLogFiles_handles_a_year_boundary()
    {
        var today = new DateOnly(2026, 1, 15);
        var files = new[]
        {
            "worklog-202601.log",
            "worklog-202512.log",
            "worklog-202511.log",
            "worklog-202510.log",
            "worklog-202509.log"
        };

        var expired = ErrorLog.SelectExpiredLogFiles(files, today);

        Assert.Equal(new[] { "worklog-202510.log", "worklog-202509.log" }, expired);
    }

    [Theory]
    [InlineData("worklog.log")]
    [InlineData("worklog-2026.log")]
    [InlineData("worklog-202613.log")]
    [InlineData("other-202601.log")]
    [InlineData("worklog-abcdef.log")]
    public void SelectExpiredLogFiles_ignores_non_matching_names(string fileName)
    {
        var expired = ErrorLog.SelectExpiredLogFiles([fileName], new DateOnly(2030, 1, 1));

        Assert.Empty(expired);
    }

    [Fact]
    public void Log_never_throws_even_with_a_null_message_scenario()
    {
        var exception = Record.Exception(() => ErrorLog.Log("Test.Context", new InvalidOperationException("x")));

        Assert.Null(exception);
    }
}
