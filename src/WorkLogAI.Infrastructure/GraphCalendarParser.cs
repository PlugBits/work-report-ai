using System.Globalization;
using System.Text.Json;
using WorkLogAI.Core;

namespace WorkLogAI.Infrastructure;

public sealed record GraphCalendarParseResult(
    IReadOnlyList<SourceEvent> Events,
    string? NextLink,
    IReadOnlyList<string> Errors);

/// <summary>
/// Pure parser for a single page of the Microsoft Graph "calendarView"
/// response. Malformed items are skipped individually so one bad record does
/// not discard the rest of the page.
/// </summary>
public static class GraphCalendarParser
{
    public static GraphCalendarParseResult Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            return new GraphCalendarParseResult(
                [], null, [$"カレンダー応答を解析できませんでした: {exception.Message}"]);
        }

        using (document)
        {
            var root = document.RootElement;
            var nextLink = ReadString(root, "@odata.nextLink");

            if (!root.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
            {
                return new GraphCalendarParseResult([], nextLink, []);
            }

            var events = new List<SourceEvent>();
            var errors = new List<string>();
            foreach (var item in value.EnumerateArray())
            {
                try
                {
                    var parsed = ParseEvent(item);
                    if (parsed is not null)
                    {
                        events.Add(parsed);
                    }
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or FormatException)
                {
                    errors.Add($"予定の1件を解析できずスキップしました: {exception.Message}");
                }
            }

            return new GraphCalendarParseResult(events, nextLink, errors);
        }
    }

    private static SourceEvent? ParseEvent(JsonElement item)
    {
        var id = ReadString(item, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException("予定IDがありません。");
        }

        if (item.TryGetProperty("isCancelled", out var cancelled)
            && cancelled.ValueKind == JsonValueKind.True)
        {
            return null;
        }

        var start = ReadDateTime(item, "start");
        if (start is null)
        {
            throw new InvalidOperationException("開始日時を解析できません。");
        }
        var end = ReadDateTime(item, "end");

        var isAllDay = item.TryGetProperty("isAllDay", out var allDayElement)
            && allDayElement.ValueKind == JsonValueKind.True;

        var subject = ReadString(item, "subject") ?? string.Empty;
        var location = ReadLocation(item);
        var evidence = BuildEvidence(isAllDay, start.Value, end, location);

        var bodyPreview = ReadString(item, "bodyPreview") ?? string.Empty;
        var body = GraphMailBodyExtractor.ExtractNewContent(bodyPreview, "text", 500);

        return SourceEventFactory.Create(
            start.Value,
            SourceTypes.Calendar,
            subject,
            body,
            evidence,
            id,
            0.5);
    }

    private static string BuildEvidence(
        bool isAllDay,
        DateTimeOffset start,
        DateTimeOffset? end,
        string location)
    {
        var prefix = isAllDay
            ? "終日"
            : end is null
                ? $"{start:HH:mm}〜"
                : $"{start:HH:mm}〜{end:HH:mm}";
        return string.IsNullOrWhiteSpace(location)
            ? prefix
            : $"{prefix} {location}";
    }

    private static DateTimeOffset? ReadDateTime(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var node) || node.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var text = ReadString(node, "dateTime");
        if (string.IsNullOrWhiteSpace(text)
            || !DateTime.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var wallClock))
        {
            return null;
        }

        // Requests are issued with `Prefer: outlook.timezone="UTC"`, so the
        // wall-clock value returned by Graph is already UTC.
        return new DateTimeOffset(DateTime.SpecifyKind(wallClock, DateTimeKind.Utc));
    }

    private static string ReadLocation(JsonElement item)
    {
        if (!item.TryGetProperty("location", out var location) || location.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }
        return ReadString(location, "displayName") ?? string.Empty;
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
