using System.Security.Cryptography;
using System.Text;

namespace WorkLogAI.Core;

public sealed record SourceCollectionResult(
    string SourceName,
    IReadOnlyList<SourceEvent> Events,
    IReadOnlyList<string> Errors)
{
    public static SourceCollectionResult Empty(string sourceName) =>
        new(sourceName, [], []);
}

public sealed record CollectorRunSummary(
    string SourceName,
    int Discovered,
    int Inserted,
    IReadOnlyList<string> Errors);

public sealed record CollectionRunResult(
    WeekRange Range,
    int InsertedEvents,
    int CandidateCount,
    IReadOnlyList<CollectorRunSummary> Sources)
{
    public IReadOnlyList<string> Errors =>
        Sources.SelectMany(source => source.Errors).ToArray();
}

public sealed class LocalCollectionCoordinator(
    IEnumerable<ISourceCollector> collectors,
    ISourceEventRepository sourceEvents,
    IReportCandidateRepository candidates,
    LocalSourceEventMapper mapper)
{
    private readonly IReadOnlyList<ISourceCollector> _collectors = collectors.ToArray();

    public async Task<CollectionRunResult> RunAsync(
        WeekRange range,
        CancellationToken cancellationToken = default)
    {
        var summaries = new List<CollectorRunSummary>();
        var totalInserted = 0;

        foreach (var collector in _collectors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SourceCollectionResult result;
            try
            {
                result = await collector.CollectAsync(range, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                summaries.Add(new CollectorRunSummary(
                    collector.SourceName,
                    0,
                    0,
                    [$"{collector.SourceName}: {exception.Message}"]));
                continue;
            }

            var inserted = 0;
            foreach (var sourceEvent in result.Events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await sourceEvents.InsertIfNewAsync(sourceEvent, cancellationToken))
                {
                    inserted++;
                }
            }

            totalInserted += inserted;
            summaries.Add(new CollectorRunSummary(
                result.SourceName,
                result.Events.Count,
                inserted,
                result.Errors));
        }

        var weeklyEvents = await sourceEvents.ListAsync(range, cancellationToken);
        var mapped = weeklyEvents.Select(sourceEvent => mapper.Map(sourceEvent, range.Start)).ToArray();
        await candidates.SaveLocalAsync(range.Start, mapped, cancellationToken);

        return new CollectionRunResult(range, totalInserted, mapped.Length, summaries);
    }
}

public sealed class LocalSourceEventMapper
{
    public ReportCandidate Map(SourceEvent sourceEvent, DateOnly weekStart)
    {
        ArgumentNullException.ThrowIfNull(sourceEvent);
        var workDate = DateOnly.FromDateTime(sourceEvent.OccurredAt.DateTime);
        var workItem = sourceEvent.SourceType switch
        {
            SourceTypes.Manual => "手動メモ",
            SourceTypes.Git => "ローカルGit",
            SourceTypes.Codex => "Codex作業",
            SourceTypes.File => "更新ファイル",
            SourceTypes.OutlookMail => "メール対応",
            SourceTypes.Calendar => "会議・予定",
            _ => "ローカル記録"
        };

        var activity = sourceEvent.SourceType == SourceTypes.Manual
            ? sourceEvent.Body
            : JoinNonBlank(sourceEvent.Title, sourceEvent.Body);

        return new ReportCandidate(
            StableIdentity.CreateGuid($"candidate\n{weekStart:yyyy-MM-dd}\n{sourceEvent.Id:D}"),
            weekStart,
            workDate,
            workItem,
            activity,
            string.Empty,
            "pending",
            sourceEvent.Confidence,
            sourceEvent.SourceType == SourceTypes.Manual,
            false,
            [sourceEvent.Id]);
    }

    private static string JoinNonBlank(string first, string second) =>
        string.Join(" — ", new[] { first, second }.Where(value => !string.IsNullOrWhiteSpace(value)));
}

public static class SourceTypes
{
    public const string Manual = "manual";
    public const string Git = "git";
    public const string Codex = "codex";
    public const string File = "file";
    public const string OutlookMail = "outlook_mail";
    public const string Calendar = "calendar";
}

public static class SourceEventFactory
{
    public static SourceEvent Create(
        DateTimeOffset occurredAt,
        string sourceType,
        string title,
        string body,
        string evidence,
        string sourceRef,
        double confidence,
        DateTimeOffset? collectedAt = null)
    {
        var canonical = string.Join(
            "\n",
            sourceType.Trim(),
            sourceRef.Trim(),
            occurredAt.ToUniversalTime().ToString("O"),
            title.Trim(),
            body.Trim(),
            evidence.Trim());
        var contentHash = StableIdentity.CreateSha256(canonical);
        return new SourceEvent(
            StableIdentity.CreateGuid(contentHash),
            occurredAt,
            sourceType.Trim(),
            title.Trim(),
            body.Trim(),
            evidence.Trim(),
            sourceRef.Trim(),
            Math.Clamp(confidence, 0, 1),
            contentHash,
            collectedAt ?? DateTimeOffset.Now);
    }
}

public static class StableIdentity
{
    public static string CreateSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static Guid CreateGuid(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        Span<byte> guidBytes = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guidBytes);
        return new Guid(guidBytes);
    }
}
