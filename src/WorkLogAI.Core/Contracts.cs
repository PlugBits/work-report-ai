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

    /// <summary>
    /// Replaces the text of an existing note in place, leaving its id, created_at and
    /// deleted_at untouched. Rejects blank text (returns <c>false</c> without writing)
    /// since the underlying column disallows it. Returns <c>false</c> for an unknown id.
    /// </summary>
    Task<bool> UpdateTextAsync(
        Guid id,
        string text,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The calendar date of the earliest quick note's <c>created_at</c> (any deleted
    /// state), or <c>null</c> when there are no notes at all. Used to seed the
    /// Obsidian sync's 全期間(バックフィル) option with the true earliest date
    /// instead of an arbitrary lookback window.
    /// </summary>
    Task<DateOnly?> GetEarliestCreatedDateAsync(CancellationToken cancellationToken = default);
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

    /// <summary>
    /// Lists the ids of every source event whose occurred_at is strictly older than
    /// <paramref name="cutoff"/>. Used by <c>DataRetentionService</c> to find deletion
    /// candidates before excluding any id still referenced by a report candidate.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListIdsOlderThanAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently excludes each given non-blank <c>source_ref</c> from ever being
    /// (re-)stored — collectors that re-read the same underlying data (git history, a
    /// live quick note) will keep rediscovering it, so deleting the stored row alone is
    /// not enough. INSERT OR IGNORE: re-suppressing an already-suppressed ref is a no-op.
    /// </summary>
    Task SuppressSourceRefsAsync(
        IReadOnlyCollection<string> refs,
        DateTimeOffset suppressedAt,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListSuppressedSourceRefsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reverses <see cref="SuppressSourceRefsAsync"/> for the given refs, so a
    /// collector can rediscover and store them again (used when a soft-deleted quick
    /// note is reopened).
    /// </summary>
    Task UnsuppressSourceRefsAsync(
        IReadOnlyCollection<string> refs,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every stored source event whose <c>source_ref</c> matches one of the
    /// given values. Returns the number of rows deleted; an empty collection is a
    /// no-op that returns 0.
    /// </summary>
    Task<int> DeleteBySourceRefsAsync(
        IReadOnlyCollection<string> refs,
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

    /// <summary>
    /// Returns the raw <c>source_event_ids_json</c> column value for every stored
    /// candidate (any week, any origin/edited/selected state). Used by
    /// <c>DataRetentionService</c> to determine which source events are still
    /// referenced before deleting old, unreferenced ones.
    /// </summary>
    Task<IReadOnlyList<string>> ListAllSourceEventIdJsonAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all candidates whose selected column is 1 and whose work_date falls
    /// within [<paramref name="fromInclusive"/>, <paramref name="toInclusive"/>],
    /// spanning any week_start, ordered by work_date. Used by the monthly summary
    /// export.
    /// </summary>
    Task<IReadOnlyList<ReportCandidate>> ListSelectedByDateRangeAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
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

    /// <summary>
    /// The calendar date of the earliest <c>meeting_sessions.started_at</c> (any
    /// status), or <c>null</c> when there are no sessions at all. <c>started_at</c>,
    /// not <c>created_at</c>, is used deliberately — <c>created_at</c> is always the
    /// row's insertion wall-clock time regardless of the meeting's actual date, so it
    /// would make a poor backfill boundary. Used to seed the Obsidian sync's
    /// 全期間(バックフィル) option alongside
    /// <see cref="IQuickNoteRepository.GetEarliestCreatedDateAsync"/>.
    /// </summary>
    Task<DateOnly?> GetEarliestStartedDateAsync(CancellationToken cancellationToken = default);
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

    /// <summary>
    /// Renders the same per-day grouped layout as <see cref="ExportAsync"/>,
    /// but for a whole calendar month spanning any number of week_start groupings —
    /// used by the monthly summary export (月次まとめ). The filename is
    /// <c>{sanitized title} 月次 {yyyyMM}.xlsx</c> and the title cell reads
    /// <c>{ReportTitle} {year}年{month}月 月次まとめ</c>.
    /// </summary>
    Task<string> ExportMonthAsync(
        int year,
        int month,
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

/// <summary>
/// The self-growing entity dictionary behind vault linking. Entities are matched
/// by canonical name OR any existing alias, case-insensitively, so a caller never
/// needs to know in advance whether a name is already known under a different
/// spelling.
/// </summary>
public interface IEntityRepository
{
    /// <summary>
    /// Merges each observation into the dictionary: a match (by canonical name or
    /// any existing alias of the observation's canonical name or its own aliases)
    /// increments <c>occurrence_count</c>, advances <c>last_seen_at</c> to
    /// <paramref name="observedAt"/>, upgrades the stored kind only when it was
    /// still <see cref="EntityKinds.Other"/>, and merges in any new aliases
    /// (an alias that already belongs to a different entity is skipped — first
    /// owner wins). A miss inserts a brand-new entity. All observations in the
    /// batch are applied within a single transaction.
    /// </summary>
    Task UpsertObservationsAsync(
        IReadOnlyList<EntityObservation> observations,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkEntity>> ListAsync(
        bool includeExcluded = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets <c>excluded</c> to match exactly <paramref name="canonicalNames"/>
    /// (case-insensitive): every entity is first un-excluded, then every entity
    /// whose canonical name appears in the list is excluded. Names with no
    /// matching entity are ignored. Used to sync the dictionary against the
    /// contents of the vault's <c>entity-exclusions.md</c> file. Transactional.
    /// </summary>
    Task ReplaceExclusionsAsync(
        IReadOnlyCollection<string> canonicalNames,
        CancellationToken cancellationToken = default);
}
