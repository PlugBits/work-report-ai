using WorkLogAI.Core;
using System.Text.Json;

namespace WorkLogAI.Infrastructure;

public sealed class CodexSessionCollector(
    string? sessionFolder,
    CodexSessionParser parser) : ISourceCollector
{
    public string SourceName => "Codexセッション";

    public async Task<SourceCollectionResult> CollectAsync(
        WeekRange range,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionFolder))
        {
            return SourceCollectionResult.Empty(SourceName);
        }

        var events = new List<SourceEvent>();
        var errors = new List<string>();
        if (!Directory.Exists(sessionFolder))
        {
            return new SourceCollectionResult(
                SourceName,
                [],
                [$"{sessionFolder}: Codexセッションフォルダーが見つかりません。"]);
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(
                sessionFolder,
                "*.jsonl",
                new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new SourceCollectionResult(SourceName, [], [exception.Message]);
        }

        foreach (var path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var session = await parser.ParseAsync(path, range, cancellationToken);
                if (session is null)
                {
                    continue;
                }

                var bodyParts = new List<string>();
                if (session.UserInstructions.Count > 0)
                {
                    bodyParts.Add($"指示: {string.Join(" / ", session.UserInstructions)}");
                }
                if (session.FinalResponses.Count > 0)
                {
                    bodyParts.Add($"完了報告: {string.Join(" / ", session.FinalResponses)}");
                }
                if (session.ChangedFiles.Count > 0)
                {
                    bodyParts.Add($"変更ファイル: {string.Join(", ", session.ChangedFiles)}");
                }
                if (session.CommandNames.Count > 0)
                {
                    bodyParts.Add($"コマンド種別: {string.Join(", ", session.CommandNames)}");
                }

                events.Add(SourceEventFactory.Create(
                    session.OccurredAt,
                    SourceTypes.Codex,
                    $"Codexセッション: {Path.GetFileNameWithoutExtension(path)}",
                    string.Join("。", bodyParts),
                    $"cwd={session.WorkingDirectory}; files={string.Join(",", session.ChangedFiles)}; commands={string.Join(",", session.CommandNames)}",
                    $"codex:{Path.GetFullPath(path)}",
                    0.75));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException)
            {
                errors.Add($"{path}: {exception.Message}");
            }
        }

        return new SourceCollectionResult(SourceName, events, errors);
    }
}
