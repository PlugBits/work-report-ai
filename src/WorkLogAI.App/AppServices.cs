using System.IO;
using System.Net.Http;
using WorkLogAI.Core;
using WorkLogAI.Infrastructure;

namespace WorkLogAI.App;

public sealed class AppServices
{
    private readonly bool _sampleMode;
    private readonly IDatabaseInitializer _database;
    private readonly IDatabasePathProvider _pathProvider;

    public AppServices(bool sampleMode)
    {
        _sampleMode = sampleMode;
        _pathProvider = new DefaultDatabasePathProvider(sampleDataMode: sampleMode);
        var connections = new SqliteConnectionFactory(_pathProvider);
        _database = new SqliteDatabaseInitializer(connections);
        Notes = new SqliteQuickNoteRepository(connections);
        SourceEvents = new SqliteSourceEventRepository(connections);
        Candidates = new SqliteReportCandidateRepository(connections);
        Meetings = new SqliteMeetingRepository(connections);
        SettingsStore = new SqliteSettingsStore(connections);
        Settings = new AppSettingsService(SettingsStore);
        Exporter = new ClosedXmlWeeklyReportExporter();
        Credentials = new WindowsCredentialStore();
        StartupRegistrar = new WindowsStartupRegistrar();
    }

    public IQuickNoteRepository Notes { get; }

    public AppSettingsService Settings { get; }

    public ISourceEventRepository SourceEvents { get; }

    public IReportCandidateRepository Candidates { get; }

    public IMeetingRepository Meetings { get; }

    public IWeeklyReportExporter Exporter { get; }

    public ICredentialStore Credentials { get; }

    public IStartupRegistrar StartupRegistrar { get; }

    public ISettingsStore SettingsStore { get; }

    public async Task InitializeAsync()
    {
        if (!_sampleMode)
        {
            RunDatabaseBackup();
        }
        await _database.InitializeAsync();
        try
        {
            await new CandidateTextCleanup(Candidates).RunAsync();
        }
        catch (Exception exception)
        {
            ErrorLog.Log("AppServices.CandidateTextCleanup", exception);
        }
        if (!_sampleMode)
        {
            try
            {
                await new DataRetentionService(SourceEvents, Candidates).RunAsync(DateTimeOffset.Now);
            }
            catch (Exception exception)
            {
                ErrorLog.Log("AppServices.DataRetention", exception);
            }
        }
        if (_sampleMode)
        {
            await new SampleDataSeeder(Notes, SourceEvents, Candidates).SeedIfEmptyAsync();
        }
    }

    /// <summary>
    /// Re-runs the same best-effort backup check the startup path uses. Called from
    /// the tray app's reminder timer once per day so a long-running instance that
    /// never restarts still gets a weekly backup opportunity. A no-op in sample mode.
    /// </summary>
    public void RunDatabaseBackupIfDue()
    {
        if (_sampleMode)
        {
            return;
        }

        RunDatabaseBackup();
    }

    private void RunDatabaseBackup()
    {
        var backupsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WorkLog AI",
            "Backups");
        new DatabaseBackupService(_pathProvider.GetDatabasePath(), backupsDirectory).RunIfNeeded();
    }

    public async Task<CollectionRunResult> CollectLocalSourcesAsync(
        WeekRange range,
        CancellationToken cancellationToken = default)
    {
        var settings = await Settings.LoadAsync(cancellationToken);
        var collectors = new List<ISourceCollector>
        {
            new ManualQuickNoteCollector(Notes),
            new LocalGitCollector(settings.LocalRepositoryPaths, new BoundedProcessRunner()),
            new CodexSessionCollector(settings.CodexSessionFolder, new CodexSessionParser()),
            new RecentFileCollector(settings.RecentFileFolders),
            // Always on: reads only already-formatted meeting summaries from the
            // local SQLite database, no external configuration required.
            new MeetingSummaryCollector(Meetings)
        };

        HttpClient? graphHttpClient = null;
        try
        {
            if (settings.GraphMailEnabled || settings.GraphCalendarEnabled)
            {
                graphHttpClient = new HttpClient();
                var auth = CreateGraphAuth(settings);
                if (settings.GraphMailEnabled)
                {
                    collectors.Add(new OutlookSentMailCollector(auth, graphHttpClient, enabled: true));
                }
                if (settings.GraphCalendarEnabled)
                {
                    collectors.Add(new OutlookCalendarCollector(auth, graphHttpClient, enabled: true));
                }
            }

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
        finally
        {
            graphHttpClient?.Dispose();
        }
    }

    public GraphAuthService CreateGraphAuth(AppSettingsSnapshot settings) =>
        CreateGraphAuth(settings.GraphClientId, settings.GraphTenantId);

    public GraphAuthService CreateGraphAuth(string clientId, string tenantId) =>
        new(clientId, tenantId, _sampleMode);

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

            // AI-shaped candidates now exist for this week's evidence — deselect any
            // still-selected, unedited local row (quick memo/meeting) whose evidence is
            // fully covered by them, so the raw local text does not also reach export.
            var weekCandidates = await Candidates.ListAsync(range.Start, cancellationToken);
            var supersededIds = LocalCandidateSuppressor.SelectSupersededIds(merged, weekCandidates);
            if (supersededIds.Count > 0)
            {
                await Candidates.SetSelectedAsync(supersededIds, false, cancellationToken);
            }
            result = result with { DeselectedLocalCount = supersededIds.Count };
        }
        return result;
    }

    public async Task<MeetingFormatResult> FormatMeetingAsync(
        MeetingSession session,
        IReadOnlyList<MeetingLine> includedLines,
        CancellationToken cancellationToken = default)
    {
        var settings = await Settings.LoadAsync(cancellationToken);
        using var httpClient = new HttpClient();
        var client = new MeetingFormatClient(httpClient, Credentials);
        return await client.FormatAsync(
            new MeetingFormatRequest(session, includedLines, settings.OpenAiModel),
            cancellationToken);
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
