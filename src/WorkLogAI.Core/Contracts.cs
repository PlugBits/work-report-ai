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

public interface IWeeklyReportExporter
{
    string CreateFileName(WeekRange range);

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

public sealed record ReportIdentity(string CompanyName, string EmployeeName)
{
    public static ReportIdentity Default { get; } = new("YAHATA USA", "太田 貴也");
}
