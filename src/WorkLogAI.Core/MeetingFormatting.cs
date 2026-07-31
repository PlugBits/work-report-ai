using System.Globalization;
using System.Text.Json.Serialization;

namespace WorkLogAI.Core;

/// <summary>
/// Request to format a meeting into <see cref="MeetingFormattedResult"/>. Lines are
/// exactly the caller-approved subset from the mandatory send preview — the client
/// never decides which lines to include.
/// </summary>
public sealed record MeetingFormatRequest(
    MeetingSession Session,
    IReadOnlyList<MeetingLine> Lines,
    string Model);

/// <summary>
/// Outcome of an AI meeting-formatting attempt. Mirrors <see cref="AiGenerationResult"/>'s
/// Succeeded/Error convention. <see cref="Error"/> is always a safe, generic Japanese
/// message — it must never echo request or response bodies.
/// </summary>
public sealed record MeetingFormatResult(MeetingFormattedResult? Formatted, string? Error = null)
{
    public bool Succeeded => Error is null && Formatted is not null;
}

public interface IMeetingFormatClient
{
    Task<MeetingFormatResult> FormatAsync(
        MeetingFormatRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Raw structured-output shape requested from the model (snake_case, per the
/// documented JSON schema). Validated and converted to <see cref="MeetingFormattedResult"/>
/// by <see cref="MeetingFormatValidator"/> before it is trusted anywhere else.</summary>
public sealed class MeetingFormatEnvelope
{
    [JsonPropertyName("summary_line")]
    public required string SummaryLine { get; init; }

    [JsonPropertyName("overview")]
    public required string Overview { get; init; }

    [JsonPropertyName("decisions")]
    public required List<MeetingFormatDecisionPayload> Decisions { get; init; }

    [JsonPropertyName("action_items")]
    public required List<MeetingFormatActionItemPayload> ActionItems { get; init; }

    [JsonPropertyName("topics")]
    public required List<MeetingFormatTopicPayload> Topics { get; init; }
}

public sealed class MeetingFormatDecisionPayload
{
    [JsonPropertyName("text")]
    public required string Text { get; init; }
}

public sealed class MeetingFormatActionItemPayload
{
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonPropertyName("owner")]
    public string? Owner { get; init; }

    [JsonPropertyName("due")]
    public string? Due { get; init; }
}

public sealed class MeetingFormatTopicPayload
{
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("detail")]
    public required string Detail { get; init; }
}

/// <summary>
/// Independent, pure validation of a model's structured output — no IO, no network.
/// Applied regardless of what the (already-strict) JSON schema allowed, since a
/// schema alone cannot enforce string length, date format, or "actually
/// non-blank". On any violation the raw meeting log is left untouched and the
/// caller gets a safe Japanese error instead of a candidate result.
/// </summary>
public static class MeetingFormatValidator
{
    private const int MaximumSummaryLineLength = 120;

    public static MeetingFormatResult Validate(MeetingFormatEnvelope? envelope)
    {
        if (envelope is null)
        {
            return new MeetingFormatResult(null, "AIの応答が空でした。");
        }

        var summaryLine = envelope.SummaryLine?.Trim() ?? string.Empty;
        if (summaryLine.Length == 0 || summaryLine.Length > MaximumSummaryLineLength)
        {
            return new MeetingFormatResult(null, "AIの応答の要約行が不正です。");
        }

        if (envelope.Decisions is null || envelope.ActionItems is null || envelope.Topics is null)
        {
            return new MeetingFormatResult(null, "AIの応答の形式が不正です。");
        }

        var decisions = new List<string>();
        foreach (var decision in envelope.Decisions)
        {
            if (string.IsNullOrWhiteSpace(decision.Text))
            {
                return new MeetingFormatResult(null, "AIの応答の決定事項が不正です。");
            }

            decisions.Add(decision.Text.Trim());
        }

        var actionItems = new List<MeetingActionItem>();
        foreach (var item in envelope.ActionItems)
        {
            if (string.IsNullOrWhiteSpace(item.Text))
            {
                return new MeetingFormatResult(null, "AIの応答の宿題が不正です。");
            }

            string? due = null;
            if (!string.IsNullOrWhiteSpace(item.Due))
            {
                if (!DateOnly.TryParseExact(
                        item.Due,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out _))
                {
                    return new MeetingFormatResult(null, "AIの応答の期限の形式が不正です。");
                }

                due = item.Due.Trim();
            }

            actionItems.Add(new MeetingActionItem(
                item.Text.Trim(),
                string.IsNullOrWhiteSpace(item.Owner) ? null : item.Owner.Trim(),
                due));
        }

        var topics = new List<MeetingTopic>();
        foreach (var topic in envelope.Topics)
        {
            if (string.IsNullOrWhiteSpace(topic.Title))
            {
                return new MeetingFormatResult(null, "AIの応答の論点が不正です。");
            }

            topics.Add(new MeetingTopic(topic.Title.Trim(), topic.Detail?.Trim() ?? string.Empty));
        }

        return new MeetingFormatResult(
            new MeetingFormattedResult(summaryLine, envelope.Overview?.Trim() ?? string.Empty, decisions, actionItems, topics),
            null);
    }
}
