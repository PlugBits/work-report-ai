using System.Text.Json.Serialization;
using System.Globalization;

namespace WorkLogAI.Core;

public sealed record AiGenerationRequest(
    WeekRange Range,
    string Model,
    IReadOnlyList<SourceEvent> SourceEvents);

public sealed record AiGenerationResult(
    IReadOnlyList<ReportCandidate> Candidates,
    bool InputTruncated,
    int SentEventCount,
    string? Error = null,
    int DeselectedLocalCount = 0)
{
    public bool Succeeded => Error is null;
}

public interface IAiCandidateGenerator
{
    Task<AiGenerationResult> GenerateAsync(
        AiGenerationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class AiCandidateEnvelope
{
    [JsonPropertyName("candidates")]
    public required List<AiCandidatePayload> Candidates { get; init; }
}

public sealed class AiCandidatePayload
{
    [JsonPropertyName("date")]
    public required string Date { get; init; }

    [JsonPropertyName("workItem")]
    public required string WorkItem { get; init; }

    [JsonPropertyName("activity")]
    public required string Activity { get; init; }

    [JsonPropertyName("resultOrNext")]
    public required string ResultOrNext { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    [JsonPropertyName("sourceEventIds")]
    public required List<string> SourceEventIds { get; init; }

    [JsonPropertyName("needsConfirmation")]
    public bool NeedsConfirmation { get; init; }

    [JsonPropertyName("confirmationQuestion")]
    public string? ConfirmationQuestion { get; init; }
}

public sealed record AiValidationResult(
    IReadOnlyList<ReportCandidate> Accepted,
    IReadOnlyList<string> Rejections);

public sealed class AiCandidateValidator
{
    private static readonly HashSet<string> AllowedStatuses =
        new(["completed", "ongoing", "pending"], StringComparer.Ordinal);

    public AiValidationResult Validate(
        AiCandidateEnvelope envelope,
        WeekRange range,
        IReadOnlyCollection<Guid> sentEventIds)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var allowedIds = sentEventIds.ToHashSet();
        var accepted = new List<ReportCandidate>();
        var rejections = new List<string>();
        var confirmationCount = 0;

        if (envelope.Candidates is null)
        {
            return new AiValidationResult([], ["Candidates collection is missing."]);
        }

        foreach (var payload in envelope.Candidates)
        {
            if (!DateOnly.TryParseExact(
                    payload.Date,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date)
                || !range.Contains(date))
            {
                rejections.Add("Candidate date is outside the requested week.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(payload.WorkItem)
                || string.IsNullOrWhiteSpace(payload.Activity)
                || payload.ResultOrNext is null)
            {
                rejections.Add("Candidate work item and activity are required.");
                continue;
            }
            if (!AllowedStatuses.Contains(payload.Status)
                || payload.Confidence is < 0 or > 1)
            {
                rejections.Add("Candidate status or confidence is invalid.");
                continue;
            }

            var ids = new List<Guid>();
            var validEvidence = payload.SourceEventIds is { Count: > 0 };
            foreach (var value in (payload.SourceEventIds ?? []).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Guid.TryParse(value, out var id) || !allowedIds.Contains(id))
                {
                    validEvidence = false;
                    break;
                }
                ids.Add(id);
            }
            if (!validEvidence || ids.Count == 0)
            {
                rejections.Add("Candidate evidence is missing or was not sent.");
                continue;
            }
            if (payload.NeedsConfirmation
                && (confirmationCount >= 3
                    || string.IsNullOrWhiteSpace(payload.ConfirmationQuestion)))
            {
                rejections.Add("Candidate confirmation question is invalid or exceeds the limit.");
                continue;
            }
            if (payload.NeedsConfirmation)
            {
                confirmationCount++;
            }
            if (!payload.NeedsConfirmation && payload.ConfirmationQuestion is not null)
            {
                rejections.Add("Unexpected confirmation question.");
                continue;
            }

            var identity = string.Join(
                "\n",
                range.Start,
                date,
                payload.WorkItem.Trim(),
                payload.Activity.Trim(),
                string.Join(",", ids.Order()));
            accepted.Add(new ReportCandidate(
                StableIdentity.CreateGuid(identity),
                range.Start,
                date,
                payload.WorkItem.Trim(),
                payload.Activity.Trim(),
                payload.ResultOrNext?.Trim() ?? string.Empty,
                payload.Status,
                payload.Confidence,
                true,
                false,
                ids,
                payload.NeedsConfirmation,
                payload.ConfirmationQuestion?.Trim(),
                CandidateOrigins.Ai));
        }

        return new AiValidationResult(accepted, rejections);
    }
}
