namespace WorkLogAI.Core;

public sealed record QuickNote
{
    public QuickNote(Guid id, DateTimeOffset createdAt, string text, DateTimeOffset? deletedAt = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A note must have an id.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Note text cannot be blank.", nameof(text));
        }

        Id = id;
        CreatedAt = createdAt;
        Text = text.Trim();
        DeletedAt = deletedAt;
    }

    public Guid Id { get; }

    public DateTimeOffset CreatedAt { get; }

    public string Text { get; }

    public DateTimeOffset? DeletedAt { get; }
}

public sealed record SourceEvent(
    Guid Id,
    DateTimeOffset OccurredAt,
    string SourceType,
    string Title,
    string Body,
    string Evidence,
    string SourceRef,
    double Confidence,
    string ContentHash,
    DateTimeOffset CollectedAt);

public sealed record ReportCandidate(
    Guid Id,
    DateOnly WeekStart,
    DateOnly WorkDate,
    string WorkItem,
    string Activity,
    string ResultOrNext,
    string Status,
    double Confidence,
    bool Selected,
    bool Edited,
    IReadOnlyList<Guid> SourceEventIds,
    bool NeedsConfirmation = false,
    string? ConfirmationQuestion = null,
    string Origin = CandidateOrigins.Local);

public static class CandidateOrigins
{
    public const string Local = "local";
    public const string Ai = "ai";
    public const string Manual = "manual";
}

public sealed record ReportRow(
    DateOnly Date,
    string WorkItem,
    string Activity,
    string ResultOrNext);

public readonly record struct WeekRange(DateOnly Start, DateOnly End)
{
    public bool Contains(DateOnly date) => date >= Start && date <= End;
}
