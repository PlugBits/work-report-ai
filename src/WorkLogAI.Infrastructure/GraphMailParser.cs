using System.Globalization;
using System.Text.Json;
using WorkLogAI.Core;

namespace WorkLogAI.Infrastructure;

public sealed record GraphMailParseResult(
    IReadOnlyList<SourceEvent> Events,
    string? NextLink,
    IReadOnlyList<string> Errors);

/// <summary>
/// Pure parser for a single page of the Microsoft Graph "sent items" messages
/// response. Malformed items are skipped individually so one bad record does
/// not discard the rest of the page.
/// </summary>
public static class GraphMailParser
{
    private static readonly string[] ExcludedSubjectPrefixes =
    [
        "自動応答:",
        "automatic reply:",
        "不在通知:",
        "accepted:",
        "declined:",
        "tentative:",
        "承諾:",
        "辞退:",
        "仮の予定:"
    ];

    public static GraphMailParseResult Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            return new GraphMailParseResult([], null, [$"メール応答を解析できませんでした: {exception.Message}"]);
        }

        using (document)
        {
            var root = document.RootElement;
            var nextLink = ReadString(root, "@odata.nextLink");

            if (!root.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
            {
                return new GraphMailParseResult([], nextLink, []);
            }

            var events = new List<SourceEvent>();
            var errors = new List<string>();
            foreach (var item in value.EnumerateArray())
            {
                try
                {
                    var parsed = ParseMessage(item);
                    if (parsed is not null)
                    {
                        events.Add(parsed);
                    }
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or FormatException)
                {
                    errors.Add($"メールの1件を解析できずスキップしました: {exception.Message}");
                }
            }

            return new GraphMailParseResult(events, nextLink, errors);
        }
    }

    private static SourceEvent? ParseMessage(JsonElement item)
    {
        var id = ReadString(item, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException("メッセージIDがありません。");
        }

        var sentDateTimeText = ReadString(item, "sentDateTime");
        if (string.IsNullOrWhiteSpace(sentDateTimeText)
            || !DateTimeOffset.TryParse(
                sentDateTimeText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var occurredAt))
        {
            throw new InvalidOperationException("送信日時を解析できません。");
        }

        var subject = ReadString(item, "subject") ?? string.Empty;
        var trimmedSubject = subject.Trim();
        if (ExcludedSubjectPrefixes.Any(
                prefix => trimmedSubject.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var contentType = "text";
        var bodyContent = string.Empty;
        if (item.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.Object)
        {
            contentType = ReadString(body, "contentType") ?? "text";
            bodyContent = ReadString(body, "content") ?? string.Empty;
        }

        var extractedBody = GraphMailBodyExtractor.ExtractNewContent(bodyContent, contentType);

        var isReply = trimmedSubject.StartsWith("re:", StringComparison.OrdinalIgnoreCase);
        if (isReply && string.IsNullOrWhiteSpace(extractedBody))
        {
            return null;
        }

        var recipients = ReadRecipientNames(item, "toRecipients")
            .Concat(ReadRecipientNames(item, "ccRecipients"))
            .ToArray();
        var evidence = recipients.Length > 0
            ? $"宛先: {string.Join(", ", recipients)}"
            : "宛先: (不明)";

        return SourceEventFactory.Create(
            occurredAt,
            SourceTypes.OutlookMail,
            subject,
            extractedBody,
            evidence,
            id,
            0.7);
    }

    private static IEnumerable<string> ReadRecipientNames(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var recipients) || recipients.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var recipient in recipients.EnumerateArray())
        {
            if (!recipient.TryGetProperty("emailAddress", out var emailAddress)
                || emailAddress.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = ReadString(emailAddress, "name");
            var address = ReadString(emailAddress, "address");
            var display = !string.IsNullOrWhiteSpace(name) ? name : address;
            if (!string.IsNullOrWhiteSpace(display))
            {
                yield return display!;
            }
        }
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
