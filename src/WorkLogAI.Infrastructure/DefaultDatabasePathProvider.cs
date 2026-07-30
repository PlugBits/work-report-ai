namespace WorkLogAI.Infrastructure;

public interface IDatabasePathProvider
{
    string GetDatabasePath();
}

public sealed class DefaultDatabasePathProvider : IDatabasePathProvider
{
    private readonly string _localApplicationData;
    private readonly bool _sampleDataMode;

    public DefaultDatabasePathProvider(string? localApplicationData = null, bool sampleDataMode = false)
    {
        _localApplicationData = localApplicationData
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _sampleDataMode = sampleDataMode;
    }

    public string GetDatabasePath()
    {
        var directory = Path.Combine(
            _localApplicationData,
            "WorkLog AI",
            _sampleDataMode ? "SampleData" : "Data");
        return Path.Combine(directory, _sampleDataMode ? "worklog-sample.db" : "worklog.db");
    }
}

public sealed class FixedDatabasePathProvider(string path) : IDatabasePathProvider
{
    public string GetDatabasePath() => path;
}
