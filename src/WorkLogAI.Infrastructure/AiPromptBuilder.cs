using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using WorkLogAI.Core;

namespace WorkLogAI.Infrastructure;

public sealed record AiPromptPackage(
    string Instructions,
    string Input,
    IReadOnlyList<Guid> IncludedEventIds,
    bool Truncated);

public sealed partial class AiPromptBuilder(
    int maximumEvents = 200,
    int maximumUtf8Bytes = 256 * 1024)
{
    public AiPromptPackage Build(WeekRange range, IReadOnlyCollection<SourceEvent> sourceEvents)
    {
        var selected = new List<object>();
        var ids = new List<Guid>();
        var truncated = false;
        foreach (var sourceEvent in sourceEvents
                     .Where(item => range.Contains(DateOnly.FromDateTime(item.OccurredAt.DateTime)))
                     .GroupBy(item => item.ContentHash, StringComparer.Ordinal)
                     .Select(group => group.OrderBy(item => item.Id).First())
                     .OrderBy(item => item.OccurredAt)
                     .ThenBy(item => item.Id))
        {
            if (selected.Count >= maximumEvents)
            {
                truncated = true;
                break;
            }
            var body = sourceEvent.SourceType == SourceTypes.Git
                ? GitEventText.StripFileList(sourceEvent.Body)
                : sourceEvent.Body;
            var item = new
            {
                eventId = sourceEvent.Id,
                occurredDate = sourceEvent.OccurredAt.ToString("yyyy-MM-dd"),
                sourceType = sourceEvent.SourceType,
                title = ReduceLocalPaths(Outbound(sourceEvent.Title, 500)),
                body = ReduceLocalPaths(Outbound(body, 2_000)),
                evidence = ReduceLocalPaths(Outbound(sourceEvent.Evidence, 1_000))
            };
            var tentative = JsonSerializer.Serialize(new
            {
                weekStart = range.Start.ToString("yyyy-MM-dd"),
                weekEnd = range.End.ToString("yyyy-MM-dd"),
                events = selected.Concat([item])
            });
            if (Encoding.UTF8.GetByteCount(tentative) > maximumUtf8Bytes)
            {
                truncated = true;
                break;
            }
            selected.Add(item);
            ids.Add(sourceEvent.Id);
        }

        var input = JsonSerializer.Serialize(new
        {
            weekStart = range.Start.ToString("yyyy-MM-dd"),
            weekEnd = range.End.ToString("yyyy-MM-dd"),
            events = selected
        });
        return new AiPromptPackage(Instructions, input, ids, truncated);
    }

    public const string Instructions = """
        Create concise Japanese weekly-report candidates using only the supplied evidence.
        Never add an achievement, decision, completion, or result unsupported by evidence.
        Calendar evidence alone must never imply completion.
        Consolidate genuinely related multi-day work when appropriate while retaining every sourceEventId.
        Translate technical detail into a business-relevant activity or outcome without inventing facts.
        Prioritize education, improvement, problem solving, inspection, decisions, system development, and equipment work.
        Omit routine or purely administrative work.
        Use at most three confirmation questions.
        Never guess or change dates.
        Every candidate must cite one or more supplied event IDs.
        """;

    private static string Outbound(string value, int maximumCharacters) =>
        SafeTextSanitizer.Sanitize(value, maximumCharacters);

    private static string ReduceLocalPaths(string value)
    {
        var reduced = WindowsPath().Replace(value, "[LOCAL_PATH]");
        return UnixPath().Replace(reduced, "[LOCAL_PATH]");
    }

    [GeneratedRegex(@"(?i)(?:[A-Z]:\\|\\\\)[^\s;,]+", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsPath();

    [GeneratedRegex(@"(?<!\w)/(?:Users|home|var|tmp|opt)/[^\s;,]+", RegexOptions.CultureInvariant)]
    private static partial Regex UnixPath();
}

public static class WeeklyCandidateJsonSchema
{
    public static JsonObject Create() =>
        new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("candidates"),
            ["properties"] = new JsonObject
            {
                ["candidates"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = Candidate()
                }
            }
        };

    private static JsonObject Candidate() =>
        new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray(
                "date", "workItem", "activity", "resultOrNext", "status", "confidence",
                "sourceEventIds", "needsConfirmation", "confirmationQuestion"),
            ["properties"] = new JsonObject
            {
                ["date"] = new JsonObject
                {
                    ["type"] = "string",
                    ["pattern"] = "^\\d{4}-\\d{2}-\\d{2}$"
                },
                ["workItem"] = new JsonObject { ["type"] = "string", ["minLength"] = 1 },
                ["activity"] = new JsonObject { ["type"] = "string", ["minLength"] = 1 },
                ["resultOrNext"] = new JsonObject { ["type"] = "string" },
                ["status"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray("completed", "ongoing", "pending")
                },
                ["confidence"] = new JsonObject
                {
                    ["type"] = "number",
                    ["minimum"] = 0,
                    ["maximum"] = 1
                },
                ["sourceEventIds"] = new JsonObject
                {
                    ["type"] = "array",
                    ["minItems"] = 1,
                    ["items"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["pattern"] = "^[0-9a-fA-F-]{36}$"
                    }
                },
                ["needsConfirmation"] = new JsonObject { ["type"] = "boolean" },
                ["confirmationQuestion"] = new JsonObject
                {
                    ["type"] = new JsonArray("string", "null")
                }
            }
        };
}
