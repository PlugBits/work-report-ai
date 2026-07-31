using System.Text;
using System.Text.Json;
using WorkLogAI.Core;
using WorkLogAI.Infrastructure;

namespace WorkLogAI.Tests;

public sealed class MeetingFormatPayloadBuilderTests
{
    private static MeetingSession Session(string title = "定例会議", string participants = "田中、鈴木") => new(
        Guid.NewGuid(),
        title,
        participants,
        MeetingKind.Meeting,
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
    public void Unchecked_lines_are_excluded_from_the_payload()
    {
        var lines = new[]
        {
            Line(1, MeetingMarker.None, "included line", 9, 0),
            Line(2, MeetingMarker.None, "excluded line", 9, 5)
        };

        var payload = MeetingFormatPayloadBuilder.Build(Session(), [lines[0]]);

        Assert.Equal(1, payload.LineCount);
        Assert.Contains("included line", payload.Input);
        Assert.DoesNotContain("excluded line", payload.Input);
    }

    [Fact]
    public void Marker_labels_are_composed_into_each_line_text()
    {
        var lines = new[]
        {
            Line(1, MeetingMarker.None, "plain", 9, 0),
            Line(2, MeetingMarker.Todo, "buy milk", 9, 5),
            Line(3, MeetingMarker.Decision, "approved", 9, 10)
        };

        var payload = MeetingFormatPayloadBuilder.Build(Session(), lines);

        Assert.Contains("09:00 plain", payload.Input);
        Assert.Contains("09:05 [宿題] buy milk", payload.Input);
        Assert.Contains("09:10 [決定] approved", payload.Input);
    }

    [Fact]
    public void Secrets_in_line_text_and_title_are_sanitized()
    {
        var session = Session(title: "会議メモ API_KEY=super-secret-title");
        var lines = new[] { Line(1, MeetingMarker.None, "session token=work-secret note", 9, 0) };

        var payload = MeetingFormatPayloadBuilder.Build(session, lines);

        Assert.DoesNotContain("super-secret-title", payload.Input);
        Assert.DoesNotContain("work-secret", payload.Input);
        Assert.Contains("[REDACTED]", payload.Input);
    }

    [Fact]
    public void Local_paths_in_line_text_are_redacted()
    {
        var lines = new[] { Line(1, MeetingMarker.None, "see C:\\Users\\person\\repo\\file.cs for detail", 9, 0) };

        var payload = MeetingFormatPayloadBuilder.Build(Session(), lines);

        Assert.DoesNotContain(@"C:\Users\person", payload.Input);
        Assert.Contains("[LOCAL_PATH]", payload.Input);
    }

    [Fact]
    public void Session_id_never_appears_in_the_payload()
    {
        var session = Session();
        var lines = new[] { Line(1, MeetingMarker.None, "note", 9, 0) };

        var payload = MeetingFormatPayloadBuilder.Build(session, lines);

        Assert.DoesNotContain(session.Id.ToString(), payload.Input);
    }

    [Fact]
    public void Byte_count_matches_the_utf8_size_of_the_serialized_input()
    {
        var lines = new[] { Line(1, MeetingMarker.None, "日本語のメモ", 9, 0) };

        var payload = MeetingFormatPayloadBuilder.Build(Session(), lines);

        Assert.Equal(Encoding.UTF8.GetByteCount(payload.Input), payload.Utf8ByteCount);
    }

    [Fact]
    public void Empty_line_selection_still_produces_a_valid_payload_with_session_header()
    {
        var payload = MeetingFormatPayloadBuilder.Build(Session(), []);

        Assert.Equal(0, payload.LineCount);
        using var document = JsonDocument.Parse(payload.Input);
        Assert.Equal(0, document.RootElement.GetProperty("lines").GetArrayLength());
        Assert.Equal("定例会議", document.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public void Included_lines_are_ordered_by_line_number_regardless_of_input_order()
    {
        var first = Line(1, MeetingMarker.None, "first", 9, 0);
        var second = Line(2, MeetingMarker.None, "second", 9, 5);

        var payload = MeetingFormatPayloadBuilder.Build(Session(), [second, first]);

        Assert.True(payload.Input.IndexOf("first", StringComparison.Ordinal)
            < payload.Input.IndexOf("second", StringComparison.Ordinal));
    }
}
