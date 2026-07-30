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

        var weekStartsOn = Enum.TryParse<DayOfWeek>(weekValue, true, out var parsed)
            ? parsed
            : DayOfWeek.Monday;

        return new AppSettingsSnapshot(
            company,
            employee,
            weekStartsOn,
            output,
            repositories,
            string.IsNullOrWhiteSpace(codexFolder) ? null : codexFolder.Trim(),
            recentFolders,
            model,
            !bool.TryParse(previewValue, out var previewEnabled) || previewEnabled);
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
    bool SendPreviewEnabled = true)
{
    public IReadOnlyList<string> LocalRepositoryPaths => ConfiguredLocalRepositoryPaths ?? [];

    public IReadOnlyList<string> RecentFileFolders => ConfiguredRecentFileFolders ?? [];
}
