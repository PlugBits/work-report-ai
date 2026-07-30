using System.Net.Http;
using WorkLogAI.Core;
using WorkLogAI.Infrastructure;

namespace WorkLogAI.App;

public sealed class AppServices
{
    private readonly bool _sampleMode;
    private readonly IDatabaseInitializer _database;

    public AppServices(bool sampleMode)
    {
        _sampleMode = sampleMode;
        var pathProvider = new DefaultDatabasePathProvider(sampleDataMode: sampleMode);
        var connections = new SqliteConnectionFactory(pathProvider);
        _database = new SqliteDatabaseInitializer(connections);
        Notes = new SqliteQuickNoteRepository(connections);
        SourceEvents = new SqliteSourceEventRepository(connections);
        Candidates = new SqliteReportCandidateRepository(connections);
        Settings = new AppSettingsService(new SqliteSettingsStore(connections));
        Exporter = new ClosedXmlWeeklyReportExporter();
        Credentials = new WindowsCredentialStore();
    }

    public IQuickNoteRepository Notes { get; }

    public AppSettingsService Settings { get; }

    public ISourceEventRepository SourceEvents { get; }

    public IReportCandidateRepository Candidates { get; }

    public IWeeklyReportExporter Exporter { get; }

    public ICredentialStore Credentials { get; }

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        if (_sampleMode)
        {
            await new SampleDataSeeder(Notes, SourceEvents, Candidates).SeedIfEmptyAsync();
        }
    }

    public async Task<CollectionRunResult> CollectLocalSourcesAsync(
        WeekRange range,
        CancellationToken cancellationToken = default)
    {
        var settings = await Settings.LoadAsync(cancellationToken);
        ISourceCollector[] collectors =
        [
            new ManualQuickNoteCollector(Notes),
            new LocalGitCollector(settings.LocalRepositoryPaths, new BoundedProcessRunner()),
            new CodexSessionCollector(settings.CodexSessionFolder, new CodexSessionParser()),
            new RecentFileCollector(settings.RecentFileFolders)
        ];
        var coordinator = new LocalCollectionCoordinator(
            collectors,
            SourceEvents,
            Candidates,
            new LocalSourceEventMapper());
        // Collectors intentionally perform bounded synchronous filesystem/process
        // parsing in places. Run the complete coordinator away from the WPF
        // dispatcher so a large configured folder cannot block the tray UI.
        return await Task.Run(
            () => coordinator.RunAsync(range, cancellationToken),
            cancellationToken);
    }

    public async Task<GenerationPreview> GetGenerationPreviewAsync(
        WeekRange range,
        CancellationToken cancellationToken = default)
    {
        var settings = await Settings.LoadAsync(cancellationToken);
        var events = await SourceEvents.ListAsync(range, cancellationToken);
        var prompt = new AiPromptBuilder().Build(range, events);
        var hasCredential = !string.IsNullOrWhiteSpace(
            await Credentials.GetAsync(CredentialTargets.OpenAiApiKey, cancellationToken));
        var sources = events
            .GroupBy(item => item.SourceType)
            .ToDictionary(group => group.Key, group => group.Count());
        return new GenerationPreview(
            settings.OpenAiModel,
            settings.SendPreviewEnabled,
            hasCredential,
            events.Count,
            prompt.IncludedEventIds.Count,
            prompt.Truncated,
            sources);
    }

    public async Task<AiGenerationResult> GenerateAiCandidatesAsync(
        WeekRange range,
        CancellationToken cancellationToken = default)
    {
        var settings = await Settings.LoadAsync(cancellationToken);
        var events = await SourceEvents.ListAsync(range, cancellationToken);
        using var httpClient = new HttpClient();
        var client = new OpenAiResponsesClient(
            httpClient,
            Credentials,
            new AiPromptBuilder(),
            new AiCandidateValidator());
        var result = await client.GenerateAsync(
            new AiGenerationRequest(range, settings.OpenAiModel, events),
            cancellationToken);
        if (result.Succeeded)
        {
            var merged = new CandidateMergeService().Merge(result.Candidates);
            result = result with { Candidates = merged };
            await Candidates.SaveGeneratedAsync(range.Start, merged, cancellationToken);
        }
        return result;
    }
}

public sealed record GenerationPreview(
    string Model,
    bool PreviewEnabled,
    bool HasCredential,
    int AvailableEvents,
    int SentEvents,
    bool Truncated,
    IReadOnlyDictionary<string, int> SourceCounts);
