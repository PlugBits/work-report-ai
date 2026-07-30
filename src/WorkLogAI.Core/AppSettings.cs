namespace WorkLogAI.Core;

public static class AppSettingKeys
{
    public const string CompanyName = "profile.company_name";
    public const string EmployeeName = "profile.employee_name";
    public const string WeekStartsOn = "report.week_starts_on";
    public const string ExcelOutputDirectory = "report.excel_output_directory";
    public const string HotKey = "capture.hot_key";
    public const string LocalRepositoryPaths = "collection.local_repository_paths";
    public const string CodexSessionFolder = "collection.codex_session_folder";
    public const string RecentFileFolders = "collection.recent_file_folders";
    public const string OpenAiModel = "ai.openai_model";
    public const string SendPreviewEnabled = "ai.send_preview_enabled";
    public const string ReminderEnabled = "reminder.enabled";
    public const string ReminderTime = "reminder.time";
    public const string ReminderLastShownDate = "reminder.last_shown_date";
    public const string GraphClientId = "graph.client_id";
    public const string GraphTenantId = "graph.tenant_id";
    public const string GraphMailEnabled = "graph.mail_enabled";
    public const string GraphCalendarEnabled = "graph.calendar_enabled";
}

public sealed class AppSettingsService(ISettingsStore store)
{
    public async Task<AppSettingsSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var company = await store.GetAsync(AppSettingKeys.CompanyName, cancellationToken)
            ?? ReportIdentity.Default.CompanyName;
        var employee = await store.GetAsync(AppSettingKeys.EmployeeName, cancellationToken)
            ?? ReportIdentity.Default.EmployeeName;
        var weekValue = await store.GetAsync(AppSettingKeys.WeekStartsOn, cancellationToken);
        var output = await store.GetAsync(AppSettingKeys.ExcelOutputDirectory, cancellationToken)
            ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var repositories = ParsePaths(
            await store.GetAsync(AppSettingKeys.LocalRepositoryPaths, cancellationToken));
        var codexFolder = await store.GetAsync(AppSettingKeys.CodexSessionFolder, cancellationToken);
        var recentFolders = ParsePaths(
            await store.GetAsync(AppSettingKeys.RecentFileFolders, cancellationToken));
        var model = await store.GetAsync(AppSettingKeys.OpenAiModel, cancellationToken)
            ?? "gpt-5.6-sol";
        var previewValue = await store.GetAsync(AppSettingKeys.SendPreviewEnabled, cancellationToken);
        var reminderEnabledValue = await store.GetAsync(AppSettingKeys.ReminderEnabled, cancellationToken);
        var reminderTimeValue = await store.GetAsync(AppSettingKeys.ReminderTime, cancellationToken);
        var graphClientId = await store.GetAsync(AppSettingKeys.GraphClientId, cancellationToken);
        var graphTenantId = await store.GetAsync(AppSettingKeys.GraphTenantId, cancellationToken);
        var graphMailEnabledValue = await store.GetAsync(AppSettingKeys.GraphMailEnabled, cancellationToken);
        var graphCalendarEnabledValue = await store.GetAsync(AppSettingKeys.GraphCalendarEnabled, cancellationToken);

        var weekStartsOn = Enum.TryParse<DayOfWeek>(weekValue, true, out var parsed)
            ? parsed
            : DayOfWeek.Monday;
        var reminderTime = TimeOnly.TryParseExact(
            reminderTimeValue,
            "HH:mm",
            out var parsedReminderTime)
            ? parsedReminderTime
            : AppSettingsSnapshot.DefaultReminderTime;

        return new AppSettingsSnapshot(
            company,
            employee,
            weekStartsOn,
            output,
            repositories,
            string.IsNullOrWhiteSpace(codexFolder) ? null : codexFolder.Trim(),
            recentFolders,
            model,
            !bool.TryParse(previewValue, out var previewEnabled) || previewEnabled,
            !bool.TryParse(reminderEnabledValue, out var reminderEnabled) || reminderEnabled,
            reminderTime,
            string.IsNullOrWhiteSpace(graphClientId) ? string.Empty : graphClientId.Trim(),
            string.IsNullOrWhiteSpace(graphTenantId) ? "common" : graphTenantId.Trim(),
            bool.TryParse(graphMailEnabledValue, out var graphMailEnabled) && graphMailEnabled,
            bool.TryParse(graphCalendarEnabledValue, out var graphCalendarEnabled) && graphCalendarEnabled);
    }

    public async Task SaveAsync(AppSettingsSnapshot settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await store.SetAsync(AppSettingKeys.CompanyName, settings.CompanyName, cancellationToken);
        await store.SetAsync(AppSettingKeys.EmployeeName, settings.EmployeeName, cancellationToken);
        await store.SetAsync(AppSettingKeys.WeekStartsOn, settings.WeekStartsOn.ToString(), cancellationToken);
        await store.SetAsync(
            AppSettingKeys.ExcelOutputDirectory,
            settings.ExcelOutputDirectory,
            cancellationToken);
        await store.SetAsync(
            AppSettingKeys.LocalRepositoryPaths,
            string.Join(Environment.NewLine, settings.LocalRepositoryPaths),
            cancellationToken);
        await store.SetAsync(
            AppSettingKeys.CodexSessionFolder,
            settings.CodexSessionFolder ?? string.Empty,
            cancellationToken);
        await store.SetAsync(
            AppSettingKeys.RecentFileFolders,
            string.Join(Environment.NewLine, settings.RecentFileFolders),
            cancellationToken);
        await store.SetAsync(
            AppSettingKeys.OpenAiModel,
            settings.OpenAiModel,
            cancellationToken);
        await store.SetAsync(
            AppSettingKeys.SendPreviewEnabled,
            settings.SendPreviewEnabled.ToString(),
            cancellationToken);
        await store.SetAsync(
            AppSettingKeys.ReminderEnabled,
            settings.ReminderEnabled.ToString(),
            cancellationToken);
        await store.SetAsync(
            AppSettingKeys.ReminderTime,
            settings.ReminderTime.ToString("HH:mm"),
            cancellationToken);
        await store.SetAsync(
            AppSettingKeys.GraphClientId,
            settings.GraphClientId,
            cancellationToken);
        await store.SetAsync(
            AppSettingKeys.GraphTenantId,
            settings.GraphTenantId,
            cancellationToken);
        await store.SetAsync(
            AppSettingKeys.GraphMailEnabled,
            settings.GraphMailEnabled.ToString(),
            cancellationToken);
        await store.SetAsync(
            AppSettingKeys.GraphCalendarEnabled,
            settings.GraphCalendarEnabled.ToString(),
            cancellationToken);
    }

    private static IReadOnlyList<string> ParsePaths(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
}

public sealed record AppSettingsSnapshot(
    string CompanyName,
    string EmployeeName,
    DayOfWeek WeekStartsOn,
    string ExcelOutputDirectory,
    IReadOnlyList<string>? ConfiguredLocalRepositoryPaths = null,
    string? CodexSessionFolder = null,
    IReadOnlyList<string>? ConfiguredRecentFileFolders = null,
    string OpenAiModel = "gpt-5.6-sol",
    bool SendPreviewEnabled = true,
    bool ReminderEnabled = true,
    TimeOnly? ConfiguredReminderTime = null,
    string GraphClientId = "",
    string GraphTenantId = "common",
    bool GraphMailEnabled = false,
    bool GraphCalendarEnabled = false)
{
    public static TimeOnly DefaultReminderTime { get; } = new(17, 0);

    public IReadOnlyList<string> LocalRepositoryPaths => ConfiguredLocalRepositoryPaths ?? [];

    public IReadOnlyList<string> RecentFileFolders => ConfiguredRecentFileFolders ?? [];

    public TimeOnly ReminderTime => ConfiguredReminderTime ?? DefaultReminderTime;
}
