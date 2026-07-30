using WorkLogAI.Core;

namespace WorkLogAI.Infrastructure;

public sealed class RecentFileCollector(IEnumerable<string> configuredFolders) : ISourceCollector
{
    private static readonly HashSet<string> ExcludedDirectories = new(
        [
            ".git",
            ".svn",
            ".hg",
            ".vs",
            ".idea",
            "bin",
            "obj",
            "node_modules",
            "dist",
            "build",
            "packages",
            "coverage",
            "TestResults"
        ],
        StringComparer.OrdinalIgnoreCase);

    private readonly IReadOnlyList<string> _folders = configuredFolders
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(path => path.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public string SourceName => "最近更新したファイル";

    public Task<SourceCollectionResult> CollectAsync(
        WeekRange range,
        CancellationToken cancellationToken = default)
    {
        var events = new List<SourceEvent>();
        var errors = new List<string>();
        foreach (var configuredPath in _folders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string folder;
            try
            {
                folder = Path.GetFullPath(configuredPath);
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                errors.Add($"{configuredPath}: {exception.Message}");
                continue;
            }
            if (!Directory.Exists(folder))
            {
                errors.Add($"{folder}: フォルダーが見つかりません。");
                continue;
            }

            CollectFolder(folder, range, events, errors, cancellationToken);
        }

        return Task.FromResult(new SourceCollectionResult(SourceName, events, errors));
    }

    private static void CollectFolder(
        string root,
        WeekRange range,
        ICollection<SourceEvent> events,
        ICollection<string> errors,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            if (directory != root && IsExcludedDirectory(directory))
            {
                continue;
            }

            try
            {
                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    try
                    {
                        var attributes = File.GetAttributes(child);
                        if ((attributes & FileAttributes.ReparsePoint) == 0
                            && !IsExcludedDirectory(child))
                        {
                            pending.Push(child);
                        }
                    }
                    catch (Exception exception) when (
                        exception is IOException or UnauthorizedAccessException)
                    {
                        errors.Add($"{child}: {exception.Message}");
                    }
                }

                foreach (var path in Directory.EnumerateFiles(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (SafePathPolicy.IsSensitive(path))
                    {
                        continue;
                    }

                    try
                    {
                        var file = new FileInfo(path);
                        var modified = new DateTimeOffset(file.LastWriteTime);
                        if (!range.Contains(DateOnly.FromDateTime(modified.DateTime)))
                        {
                            continue;
                        }

                        var type = string.IsNullOrEmpty(file.Extension)
                            ? "拡張子なし"
                            : file.Extension.TrimStart('.').ToUpperInvariant();
                        events.Add(SourceEventFactory.Create(
                            modified,
                            SourceTypes.File,
                            file.Name,
                            $"種別: {type} / 更新日時: {modified:yyyy/MM/dd HH:mm}",
                            $"path={file.FullName}; type={type}; modified={modified:O}",
                            $"file:{file.FullName}",
                            0.45));
                    }
                    catch (Exception exception) when (
                        exception is IOException or UnauthorizedAccessException)
                    {
                        errors.Add($"{path}: {exception.Message}");
                    }
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{directory}: {exception.Message}");
            }
        }
    }

    private static bool IsExcludedDirectory(string path) =>
        ExcludedDirectories.Contains(Path.GetFileName(
            path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
}
