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

    /// <summary>
    /// Conservative per-batch UTF-8 byte budget for the vault sync's entity-extraction
    /// requests: well under <see cref="EntityExtractionPayloadBuilder.MaximumUtf8Bytes"/>
    /// (64 KiB) to leave margin for the JSON envelope and known-names list that
    /// <see cref="EntityExtractionPayloadBuilder.Build"/> adds on top of the raw text.
    /// </summary>
    private const int VaultSyncBatchMaxUtf8Bytes = 40 * 1024;

    private const int VaultSyncMaxExtractionErrorMessages = 5;

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
        Entities = new SqliteEntityRepository(connections);
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

    public IEntityRepository Entities { get; }

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

    /// <summary>
    /// Loads the current dictionary and resolves it into link targets using the
    /// configured occurrence threshold — used both by the vault daily-note sync and
    /// by meeting Markdown export, so every generated document links against exactly
    /// the same dictionary state at the moment of export.
    /// </summary>
    public async Task<IReadOnlyList<EntityLinkTarget>> GetEntityLinkTargetsAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await Settings.LoadAsync(cancellationToken);
        var entities = await Entities.ListAsync(cancellationToken: cancellationToken);
        return EntityLinkTargets.From(entities, settings.VaultEntityLinkMinOccurrences);
    }

    /// <summary>
    /// The earliest date the 全期間(バックフィル) vault sync option should start from:
    /// the earliest of the earliest quick note and the earliest meeting session, or
    /// today when there is no data at all.
    /// </summary>
    public async Task<DateOnly> GetVaultBackfillStartDateAsync(CancellationToken cancellationToken = default)
    {
        var noteDate = await Notes.GetEarliestCreatedDateAsync(cancellationToken);
        var meetingDate = await Meetings.GetEarliestStartedDateAsync(cancellationToken);
        var earliest = noteDate;
        if (meetingDate is { } candidate && (earliest is null || candidate < earliest))
        {
            earliest = candidate;
        }

        return earliest ?? DateOnly.FromDateTime(DateTime.Today);
    }

    /// <summary>
    /// Counts exactly the texts <see cref="SyncVaultAsync"/> would send for AI entity
    /// extraction over <paramref name="fromDate"/>–<paramref name="toDate"/>, without
    /// sending anything — used by the tray sync window's mandatory send-preview
    /// confirmation so the shown count is never a guess.
    /// </summary>
    public async Task<int> CountVaultExtractionTextsAsync(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        var range = OrderedRange(fromDate, toDate);
        var (rangeFrom, rangeTo) = WeekRangeTimeBounds.Local(range);
        var notes = await Notes.ListAsync(rangeFrom, rangeTo, includeDeleted: false, cancellationToken: cancellationToken);
        var meetings = await Meetings.ListFormattedInRangeAsync(range, cancellationToken);
        return VaultExtractionTextGatherer.Gather(notes, meetings).Count;
    }

    /// <summary>
    /// Syncs the Obsidian vault for <paramref name="fromDate"/>–<paramref name="toDate"/>
    /// inclusive: syncs the exclusion file into the dictionary, optionally runs AI
    /// entity extraction over the range's memo/meeting text to grow the dictionary,
    /// then regenerates and overwrites each day's daily note (see
    /// <see cref="DailyNoteWriter"/> — daily notes are a one-way generated artifact,
    /// never hand-merged). <paramref name="progress"/> receives short Japanese status
    /// updates suitable for a modal progress window.
    /// </summary>
    public async Task<VaultSyncResult> SyncVaultAsync(
        DateOnly fromDate,
        DateOnly toDate,
        bool runExtraction,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var settings = await Settings.LoadAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.VaultDailyNotesFolder))
        {
            return VaultSyncResult.Failure("デイリーノート出力フォルダが未設定です。");
        }

        var range = OrderedRange(fromDate, toDate);

        // The loop below performs bounded synchronous file IO and, when extraction is
        // enabled, several sequential network calls — run it away from the WPF
        // dispatcher exactly like CollectLocalSourcesAsync does for local collection.
        return await Task.Run(
            () => SyncVaultCoreAsync(settings, range.Start, range.End, runExtraction, progress, cancellationToken),
            cancellationToken);
    }

    private async Task<VaultSyncResult> SyncVaultCoreAsync(
        AppSettingsSnapshot settings,
        DateOnly fromDate,
        DateOnly toDate,
        bool runExtraction,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        await SyncVaultExclusionsAsync(settings.VaultDailyNotesFolder, cancellationToken);

        var range = new WeekRange(fromDate, toDate);
        var (rangeFrom, rangeTo) = WeekRangeTimeBounds.Local(range);
        var quickNotes = await Notes.ListAsync(rangeFrom, rangeTo, includeDeleted: false, cancellationToken: cancellationToken);
        var formattedMeetings = await Meetings.ListFormattedInRangeAsync(range, cancellationToken);
        var gitEvents = (await SourceEvents.ListAsync(range, cancellationToken))
            .Where(sourceEvent => sourceEvent.SourceType == SourceTypes.Git)
            .ToArray();
        var selectedCandidates = await Candidates.ListSelectedByDateRangeAsync(fromDate, toDate, cancellationToken);

        var extractedBatches = 0;
        var extractionErrors = new List<string>();
        if (runExtraction)
        {
            (extractedBatches, extractionErrors) = await RunVaultExtractionAsync(
                settings, quickNotes, formattedMeetings, progress, cancellationToken);
        }

        var allEntities = await Entities.ListAsync(cancellationToken: cancellationToken);
        var linkTargets = EntityLinkTargets.From(allEntities, settings.VaultEntityLinkMinOccurrences);
        string LinkText(string text) => EntityLinker.Link(text, linkTargets);

        var writer = new DailyNoteWriter();
        var writtenDays = 0;
        for (var date = fromDate; date <= toDate; date = date.AddDays(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"デイリーノートを書き出し中… ({date:yyyy-MM-dd})");

            var dayNotes = quickNotes
                .Where(note => DateOnly.FromDateTime(note.CreatedAt.DateTime) == date)
                .ToArray();
            var daySessions = formattedMeetings
                .Where(meeting => DateOnly.FromDateTime(meeting.Session.StartedAt.DateTime) == date)
                .Select(meeting => meeting.Session)
                .ToArray();
            var dayMeetings = DailyNoteMeetingBuilder.Build(date, daySessions);
            var dayGitEvents = gitEvents
                .Where(sourceEvent => DateOnly.FromDateTime(sourceEvent.OccurredAt.DateTime) == date)
                .ToArray();
            var dayCandidateLines = selectedCandidates
                .Where(candidate => candidate.WorkDate == date)
                .Select(candidate => (candidate.WorkItem, candidate.Activity))
                .ToArray();

            var content = DailyNoteBuilder.Build(
                date, dayNotes, dayMeetings, dayGitEvents, dayCandidateLines, LinkText);
            if (content is null)
            {
                continue;
            }

            try
            {
                writer.Write(settings.VaultDailyNotesFolder, date, content);
                writtenDays++;
            }
            catch (Exception exception)
            {
                ErrorLog.Log("AppServices.VaultSync.Write", exception);
            }
        }

        return new VaultSyncResult(
            writtenDays,
            extractedBatches,
            extractionErrors,
            allEntities.Count,
            linkTargets.Count);
    }

    private async Task SyncVaultExclusionsAsync(string vaultFolder, CancellationToken cancellationToken)
    {
        try
        {
            var path = Path.Combine(vaultFolder, "entity-exclusions.md");
            if (!File.Exists(path))
            {
                return;
            }

            var content = await File.ReadAllTextAsync(path, cancellationToken);
            var names = ExclusionFileParser.Parse(content);
            await Entities.ReplaceExclusionsAsync(names, cancellationToken);
        }
        catch (Exception exception)
        {
            ErrorLog.Log("AppServices.VaultSync.Exclusions", exception);
        }
    }

    private async Task<(int Batches, List<string> Errors)> RunVaultExtractionAsync(
        AppSettingsSnapshot settings,
        IReadOnlyList<QuickNote> quickNotes,
        IReadOnlyList<(MeetingSession Session, MeetingSummary Summary)> formattedMeetings,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var texts = VaultExtractionTextGatherer.Gather(quickNotes, formattedMeetings);
        if (texts.Count == 0)
        {
            return (0, errors);
        }

        var known = (await Entities.ListAsync(cancellationToken: cancellationToken))
            .OrderByDescending(entity => entity.OccurrenceCount)
            .Take(EntityExtractionPayloadBuilder.MaximumKnownNames)
            .Select(entity => entity.CanonicalName)
            .ToArray();

        var batches = EntityExtractionBatcher.Batch(
            texts, EntityExtractionPayloadBuilder.MaximumTexts, VaultSyncBatchMaxUtf8Bytes);

        using var httpClient = new HttpClient();
        var client = new EntityExtractionClient(httpClient, Credentials);
        var observedAt = DateTimeOffset.Now;
        var succeeded = 0;
        for (var index = 0; index < batches.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"辞書を更新中… ({index + 1}/{batches.Count})");
            var result = await client.ExtractAsync(
                new EntityExtractionRequest(known, batches[index], settings.OpenAiModel), cancellationToken);
            if (result.Succeeded)
            {
                await Entities.UpsertObservationsAsync(result.Observations!, observedAt, cancellationToken);
                succeeded++;
            }
            else if (errors.Count < VaultSyncMaxExtractionErrorMessages)
            {
                errors.Add(result.Error ?? "AIによる抽出でエラーが発生しました。");
            }
        }

        return (succeeded, errors);
    }

    private static WeekRange OrderedRange(DateOnly fromDate, DateOnly toDate) =>
        toDate < fromDate ? new WeekRange(toDate, fromDate) : new WeekRange(fromDate, toDate);
}

public sealed record GenerationPreview(
    string Model,
    bool PreviewEnabled,
    bool HasCredential,
    int AvailableEvents,
    int SentEvents,
    bool Truncated,
    IReadOnlyDictionary<string, int> SourceCounts);
