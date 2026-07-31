namespace WorkLogAI.Core;

public interface IQuickNoteRepository
{
    Task<QuickNote> CreateAsync(
        string text,
        DateTimeOffset? createdAt = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<QuickNote>> ListAsync(
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default);

    Task<bool> SoftDeleteAsync(
        Guid id,
        DateTimeOffset? deletedAt = null,
        CancellationToken cancellationToken = default);

    Task<bool> ReopenAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface ISettingsStore
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);
}

public interface ISourceEventRepository
{
    Task<bool> InsertIfNewAsync(
        SourceEvent sourceEvent,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourceEvent>> ListAsync(
        WeekRange range,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourceEvent>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default);

    Task<int> DeleteByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default);
}

public interface IReportCandidateRepository
{
    Task ReplaceWeekAsync(
        DateOnly weekStart,
        IReadOnlyCollection<ReportCandidate> candidates,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReportCandidate>> ListAsync(
        DateOnly weekStart,
        CancellationToken cancellationToken = default);

    Task SaveGeneratedAsync(
        DateOnly weekStart,
        IReadOnlyCollection<ReportCandidate> generatedCandidates,
        CancellationToken cancellationToken = default);

    Task SaveLocalAsync(
        DateOnly weekStart,
        IReadOnlyCollection<ReportCandidate> localCandidates,
        CancellationToken cancellationToken = default);

    Task SaveReviewAsync(
        DateOnly weekStart,
        IReadOnlyCollection<ReportCandidate> candidates,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates only the selected column for the given candidate ids. Other columns
    /// (including origin/edited) are left untouched. Returns the number of rows
    /// actually updated; an empty id collection is a no-op that returns 0.
    /// </summary>
    Task<int> SetSelectedAsync(
        IReadOnlyCollection<Guid> ids,
        bool selected,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the id and activity text of every stored candidate (any week, any
    /// origin/edited/selected state) whose activity contains <paramref name="needle"/>.
    /// Used by the one-time startup cleanup that strips leftover git file-list text
    /// from candidates stored before <see cref="GitEventText.StripFileList"/> covered
    /// merged rows.
    /// </summary>
    Task<IReadOnlyList<(Guid Id, string Activity)>> ListActivitiesContainingAsync(
        string needle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates only the activity column for the candidate with the given id. All
    /// other columns (including edited/selected/origin) are left untouched.
    /// </summary>
    Task UpdateActivityAsync(
        Guid id,
        string activity,
        CancellationToken cancellationToken = default);
}

public interface ISourceCollector
{
    string SourceName { get; }

    Task<SourceCollectionResult> CollectAsync(
        WeekRange range,
        CancellationToken cancellationToken = default);
}

public interface IDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public interface IMeetingRepository
{
    Task<MeetingSession> CreateSessionAsync(
        string title,
        string participants,
        MeetingKind kind,
        DateTimeOffset? startedAt = null,
        CancellationToken cancellationToken = default);

    Task UpdateSessionAsync(
        Guid sessionId,
        string title,
        string participants,
        MeetingKind kind,
        MeetingStatus status,
        DateTimeOffset? endedAt,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MeetingSession>> ListSessionsAsync(
        MeetingStatus? status = null,
        CancellationToken cancellationToken = default);

    Task<MeetingSession?> GetSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>Appends a line, assigning the next sequential line_no for the session.</summary>
    Task<MeetingLine> AddLineAsync(
        Guid sessionId,
        MeetingMarker marker,
        string text,
        DateTimeOffset? loggedAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>Updates marker/text in place. line_no is never renumbered.</summary>
    Task UpdateLineAsync(
        Guid lineId,
        MeetingMarker marker,
        string text,
        CancellationToken cancellationToken = default);

    Task DeleteLineAsync(Guid lineId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MeetingLine>> ListLinesAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>Inserts a new summary row and sets the session's status to
    /// <see cref="MeetingStatus.Formatted"/>. Prior summary rows for the session are
    /// kept (not overwritten) — <see cref="GetLatestSummaryAsync"/> returns the
    /// newest one.</summary>
    Task SaveSummaryAsync(
        Guid sessionId,
        string formattedJson,
        string summaryLine,
        CancellationToken cancellationToken = default);

    Task<MeetingSummary?> GetLatestSummaryAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>Sessions whose started_at falls within range and whose status is
    /// <see cref="MeetingStatus.Formatted"/>, each paired with its latest summary.
    /// Used by MeetingSummaryCollector — only this pairing (never raw lines) may
    /// reach the weekly AI generation pipeline.</summary>
    Task<IReadOnlyList<(MeetingSession Session, MeetingSummary Summary)>> ListFormattedInRangeAsync(
        WeekRange range,
        CancellationToken cancellationToken = default);
}

public interface ICredentialStore
{
    Task<string?> GetAsync(string target, CancellationToken cancellationToken = default);

    Task SetAsync(string target, string secret, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string target, CancellationToken cancellationToken = default);
}

public static class CredentialTargets
{
    public const string OpenAiApiKey = "WorkLog AI/OpenAI API Key";
}

public interface IGraphTokenProvider
{
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

public interface IWeeklyReportExporter
{
    string CreateFileName(WeekRange range, ReportIdentity identity);

    Task<string> ExportAsync(
        WeekRange range,
        IEnumerable<ReportRow> rows,
        string outputDirectory,
        ReportIdentity identity,
        CancellationToken cancellationToken = default);
}

public interface IStartupRegistrar
{
    bool IsEnabled();

    void Enable();

    void Disable();
}

public sealed record ReportIdentity(string CompanyName, string EmployeeName, string ReportTitle = "業務週報")
{
    public static ReportIdentity Default { get; } = new("会社名", "氏名");
}
