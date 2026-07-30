using System.Text.Json;
using WorkLogAI.Core;
using WorkLogAI.Infrastructure;

namespace WorkLogAI.Tests;

public sealed class CodexSessionParserTests
{
    [Fact]
    public async Task Strict_allowlist_keeps_only_safe_session_user_final_files_and_command_names()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "session.jsonl");
        var timestamp = new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.FromHours(-4));
        var lines = new[]
        {
            Json(new
            {
                timestamp = timestamp.ToString("O"),
                type = "session_meta",
                payload = new { cwd = @"C:\work\safe" }
            }),
            Message(timestamp, "user", "input_text",
                "Implement safe parser api_key=abc123456 ```csharp SOURCE_CODE_MARKER ```"),
            Json(new
            {
                timestamp = timestamp.ToString("O"),
                type = "response_item",
                payload = new { type = "reasoning", text = "REASONING_MARKER" }
            }),
            Json(new { timestamp = timestamp.ToString("O"), type = "compacted", payload = new { text = "COMPACTED_MARKER" } }),
            Json(new
            {
                timestamp = timestamp.ToString("O"),
                type = "response_item",
                payload = new { type = "function_call_output", output = "FUNCTION_OUTPUT_MARKER" }
            }),
            Json(new
            {
                timestamp = timestamp.ToString("O"),
                type = "response_item",
                payload = new
                {
                    type = "function_call",
                    name = "shell_command",
                    arguments = "echo FUNCTION_ARGUMENT_MARKER"
                }
            }),
            Json(new
            {
                timestamp = timestamp.ToString("O"),
                type = "response_item",
                payload = new
                {
                    type = "message",
                    role = "assistant",
                    phase = "analysis",
                    content = new[] { new { type = "output_text", text = "NON_FINAL_MARKER" } }
                }
            }),
            Json(new
            {
                timestamp = timestamp.ToString("O"),
                type = "response_item",
                payload = new
                {
                    type = "message",
                    role = "assistant",
                    phase = "final_answer",
                    content = new[] { new { type = "output_text", text = "Completed safe parser." } }
                }
            }),
            Json(new
            {
                timestamp = timestamp.ToString("O"),
                type = "response_item",
                payload = new
                {
                    type = "file_changes",
                    paths = new[] { "src/SafeParser.cs", ".env", "client-secret.pem" },
                    source = "FILE_SOURCE_CONTENT_MARKER"
                }
            }),
            "{ malformed json"
        };
        await File.WriteAllLinesAsync(path, lines);
        var range = new WeekRange(new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 2));

        var parsed = Assert.IsType<ParsedCodexSession>(
            await new CodexSessionParser().ParseAsync(path, range));

        Assert.Equal(@"C:\work\safe", parsed.WorkingDirectory);
        Assert.Contains(parsed.UserInstructions, value => value.Contains("Implement safe parser"));
        Assert.Contains("[REDACTED]", Assert.Single(parsed.UserInstructions));
        Assert.Contains("Completed safe parser.", parsed.FinalResponses);
        Assert.Contains("src/SafeParser.cs", parsed.ChangedFiles);
        Assert.DoesNotContain(".env", parsed.ChangedFiles);
        Assert.DoesNotContain("client-secret.pem", parsed.ChangedFiles);
        Assert.Equal(["shell_command"], parsed.CommandNames);
        var selected = string.Join(
            " ",
            parsed.UserInstructions
                .Concat(parsed.FinalResponses)
                .Concat(parsed.ChangedFiles)
                .Concat(parsed.CommandNames)
                .Append(parsed.WorkingDirectory));
        Assert.DoesNotContain("REASONING_MARKER", selected);
        Assert.DoesNotContain("COMPACTED_MARKER", selected);
        Assert.DoesNotContain("FUNCTION_OUTPUT_MARKER", selected);
        Assert.DoesNotContain("FUNCTION_ARGUMENT_MARKER", selected);
        Assert.DoesNotContain("NON_FINAL_MARKER", selected);
        Assert.DoesNotContain("FILE_SOURCE_CONTENT_MARKER", selected);
        Assert.DoesNotContain("SOURCE_CODE_MARKER", selected);
        Assert.DoesNotContain("abc123456", selected);
    }

    [Fact]
    public async Task Oversized_and_malformed_lines_are_skipped_without_losing_later_allowed_records()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "oversized.jsonl");
        var timestamp = DateTimeOffset.Now;
        var oversized = Message(
            timestamp,
            "user",
            "input_text",
            new string('x', CodexSessionParser.MaximumLineBytes + 1) + "OVERSIZED_MARKER");
        await File.WriteAllLinesAsync(
            path,
            [
                Json(new
                {
                    timestamp = timestamp.ToString("O"),
                    type = "session_meta",
                    payload = new { cwd = @"C:\safe" }
                }),
                oversized,
                "{broken",
                Message(timestamp, "user", "input_text", "retained instruction")
            ]);
        var range = new WeekRangeCalculator().GetWeekRange(
            DateOnly.FromDateTime(timestamp.DateTime));

        var parsed = Assert.IsType<ParsedCodexSession>(
            await new CodexSessionParser().ParseAsync(path, range));

        Assert.Contains("retained instruction", parsed.UserInstructions);
        Assert.DoesNotContain(parsed.UserInstructions, text => text.Contains("OVERSIZED_MARKER"));
    }

    [Fact]
    public async Task Selected_content_stops_at_256KiB_cap()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "capped.jsonl");
        var timestamp = DateTimeOffset.Now;
        var lines = new List<string>
        {
            Json(new
            {
                timestamp = timestamp.ToString("O"),
                type = "session_meta",
                payload = new { cwd = @"C:\safe" }
            })
        };
        for (var index = 0; index < 600; index++)
        {
            lines.Add(Message(
                timestamp,
                "user",
                "input_text",
                $"{index:D2}-" + new string('a', 3_990)));
        }
        lines.Add(Message(timestamp, "user", "input_text", "AFTER_CAP_MARKER"));
        await File.WriteAllLinesAsync(path, lines);
        var range = new WeekRangeCalculator().GetWeekRange(
            DateOnly.FromDateTime(timestamp.DateTime));

        var parsed = Assert.IsType<ParsedCodexSession>(
            await new CodexSessionParser().ParseAsync(path, range));

        Assert.True(parsed.ContentLimitReached);
        Assert.InRange(parsed.SelectedUtf8Bytes, 1, CodexSessionParser.MaximumSelectedBytes);
        Assert.DoesNotContain(parsed.UserInstructions, text => text.Contains("AFTER_CAP_MARKER"));
    }

    [Fact]
    public async Task Session_outside_requested_week_is_ignored()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "old.jsonl");
        var timestamp = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
        await File.WriteAllLinesAsync(
            path,
            [
                Json(new
                {
                    timestamp = timestamp.ToString("O"),
                    type = "session_meta",
                    payload = new { cwd = @"C:\old" }
                }),
                Message(timestamp, "user", "input_text", "old instruction")
            ]);

        var parsed = await new CodexSessionParser().ParseAsync(
            path,
            new WeekRange(new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 2)));

        Assert.Null(parsed);
    }

    private static string Message(
        DateTimeOffset timestamp,
        string role,
        string contentType,
        string text) =>
        Json(new
        {
            timestamp = timestamp.ToString("O"),
            type = "response_item",
            payload = new
            {
                type = "message",
                role,
                content = new[] { new { type = contentType, text } }
            }
        });

    private static string Json(object value) => JsonSerializer.Serialize(value);
}
