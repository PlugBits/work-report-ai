using System.Globalization;
using System.Text.RegularExpressions;
using WorkLogAI.Core;

namespace WorkLogAI.Infrastructure;

public sealed class LocalGitCollector(
    IEnumerable<string> repositoryPaths,
    IProcessRunner processRunner,
    string gitExecutable = "git") : ISourceCollector
{
    private readonly IReadOnlyList<string> _repositoryPaths = repositoryPaths
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(path => path.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public string SourceName => "ローカルGit";

    public async Task<SourceCollectionResult> CollectAsync(
        WeekRange range,
        CancellationToken cancellationToken = default)
    {
        var events = new List<SourceEvent>();
        var errors = new List<string>();
        foreach (var configuredPath in _repositoryPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var repository = Path.GetFullPath(configuredPath);
                await CollectRepositoryAsync(repository, range, events, errors, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                errors.Add($"{configuredPath}: {exception.Message}");
            }
        }

        return new SourceCollectionResult(SourceName, events, errors);
    }

    private async Task CollectRepositoryAsync(
        string repository,
        WeekRange range,
        ICollection<SourceEvent> events,
        ICollection<string> errors,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(repository))
        {
            errors.Add($"{repository}: フォルダーが見つかりません。");
            return;
        }

        var check = await GitAsync(repository, ["rev-parse", "--is-inside-work-tree"], cancellationToken);
        if (check.TimedOut || check.ExitCode != 0
            || !check.StandardOutput.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{repository}: Gitリポジトリではないか、確認できませんでした。");
            return;
        }

        var email = await ReadConfigAsync(repository, "user.email", cancellationToken);
        var name = await ReadConfigAsync(repository, "user.name", cancellationToken);
        var bounds = WeekRangeTimeBounds.Local(range);
        var arguments = new List<string>
        {
            "log",
            $"--since={bounds.From:O}",
            $"--until={bounds.To:O}",
            "--no-merges",
            "--format=%H%x1f%cI%x1f%s%x1f%b%x1e"
        };
        var author = !string.IsNullOrWhiteSpace(email) ? email : name;
        if (!string.IsNullOrWhiteSpace(author))
        {
            arguments.Add($"--author={Regex.Escape(author)}");
        }

        var log = await GitAsync(repository, arguments, cancellationToken);
        if (log.TimedOut || log.ExitCode != 0)
        {
            errors.Add($"{repository}: Git履歴の取得に失敗しました。");
            return;
        }

        foreach (var record in log.StandardOutput.Split(
                     '\u001e',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = record.Split('\u001f');
            if (fields.Length < 3
                || !DateTimeOffset.TryParse(fields[1], CultureInfo.InvariantCulture, out var occurredAt)
                || !range.Contains(DateOnly.FromDateTime(occurredAt.DateTime)))
            {
                continue;
            }

            var hash = fields[0].Trim();
            var subject = SafeTextSanitizer.Sanitize(fields[2], 500);
            var bodySummary = fields.Length > 3
                ? SafeTextSanitizer.Sanitize(fields[3], 700)
                : string.Empty;
            var statistics = await ReadStatisticsAsync(repository, hash, cancellationToken);
            var changed = statistics.Files.Take(100).ToArray();
            var bodyParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(bodySummary))
            {
                bodyParts.Add(bodySummary);
            }
            if (changed.Length > 0)
            {
                bodyParts.Add($"変更ファイル: {string.Join(", ", changed)}");
            }
            bodyParts.Add($"統計: +{statistics.Added} / -{statistics.Deleted}");

            events.Add(SourceEventFactory.Create(
                occurredAt,
                SourceTypes.Git,
                subject,
                string.Join("。", bodyParts),
                $"repository={repository}; commit={hash}; files={string.Join(",", changed)}",
                $"git:{repository}:{hash}",
                0.9));
        }

        if (range.Contains(DateOnly.FromDateTime(DateTime.Now)))
        {
            await CollectUncommittedAsync(repository, events, errors, cancellationToken);
        }
    }

    private async Task CollectUncommittedAsync(
        string repository,
        ICollection<SourceEvent> events,
        ICollection<string> errors,
        CancellationToken cancellationToken)
    {
        var status = await GitAsync(
            repository,
            ["status", "--porcelain=v1", "-z", "--untracked-files=all"],
            cancellationToken);
        if (status.TimedOut || status.ExitCode != 0)
        {
            errors.Add($"{repository}: 未コミット状態を取得できませんでした。");
            return;
        }

        var files = status.StandardOutput
            .Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry => entry.Length > 3 ? entry[3..] : entry)
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .Where(entry => !SafePathPolicy.IsSensitive(entry))
            .Distinct(StringComparer.Ordinal)
            .Take(200)
            .ToArray();
        if (files.Length == 0)
        {
            return;
        }

        var today = DateTime.Today;
        events.Add(SourceEventFactory.Create(
            new DateTimeOffset(today, TimeZoneInfo.Local.GetUtcOffset(today)),
            SourceTypes.Git,
            "未コミット変更",
            $"変更ファイル: {string.Join(", ", files)}",
            $"repository={repository}; uncommitted files={string.Join(",", files)}",
            $"git:{repository}:working-tree",
            0.85));
    }

    private async Task<(IReadOnlyList<string> Files, int Added, int Deleted)> ReadStatisticsAsync(
        string repository,
        string hash,
        CancellationToken cancellationToken)
    {
        var result = await GitAsync(
            repository,
            ["show", "--numstat", "--format=", "--no-renames", hash],
            cancellationToken);
        if (result.TimedOut || result.ExitCode != 0)
        {
            return ([], 0, 0);
        }

        var files = new List<string>();
        var added = 0;
        var deleted = 0;
        foreach (var line in result.StandardOutput.Split(
                     ['\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = line.Split('\t');
            if (fields.Length < 3)
            {
                continue;
            }

            if (SafePathPolicy.IsSensitive(fields[2]))
            {
                continue;
            }
            files.Add(fields[2]);
            if (int.TryParse(fields[0], out var lineAdded))
            {
                added += lineAdded;
            }
            if (int.TryParse(fields[1], out var lineDeleted))
            {
                deleted += lineDeleted;
            }
        }

        return (files, added, deleted);
    }

    private async Task<string?> ReadConfigAsync(
        string repository,
        string key,
        CancellationToken cancellationToken)
    {
        var result = await GitAsync(repository, ["config", "--get", key], cancellationToken);
        return result.ExitCode == 0 && !result.TimedOut
            ? result.StandardOutput.Trim()
            : null;
    }

    private Task<ProcessResult> GitAsync(
        string repository,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken) =>
        processRunner.RunAsync(
            new ProcessRequest(
                gitExecutable,
                ["-C", repository, .. arguments],
                Timeout: TimeSpan.FromSeconds(15)),
            cancellationToken);
}
