using System.Text.Json;
using WorkLogAI.Core;

namespace WorkLogAI.Infrastructure;

/// <summary>
/// Deletes source events older than a retention window, but only when no stored
/// report candidate (any week, any origin/edited/selected state) still references
/// them — a source event backing a candidate the user has kept must never
/// disappear out from under it. Never deletes report candidates themselves.
/// </summary>
public sealed class DataRetentionService(
    ISourceEventRepository sourceEvents,
    IReportCandidateRepository candidates)
{
    public static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(180);

    public async Task<int> RunAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var cutoff = now - RetentionWindow;
        var oldIds = await sourceEvents.ListIdsOlderThanAsync(cutoff, cancellationToken);
        if (oldIds.Count == 0)
        {
            return 0;
        }

        var referencedIds = await LoadReferencedIdsAsync(cancellationToken);
        var deletable = oldIds.Where(id => !referencedIds.Contains(id)).ToArray();
        if (deletable.Length == 0)
        {
            return 0;
        }

        return await sourceEvents.DeleteByIdsAsync(deletable, cancellationToken);
    }

    private async Task<HashSet<Guid>> LoadReferencedIdsAsync(CancellationToken cancellationToken)
    {
        var referenced = new HashSet<Guid>();
        var rawJsonValues = await candidates.ListAllSourceEventIdJsonAsync(cancellationToken);
        foreach (var rawJson in rawJsonValues)
        {
            Guid[]? ids;
            try
            {
                ids = JsonSerializer.Deserialize<Guid[]>(rawJson);
            }
            catch (JsonException)
            {
                continue;
            }

            if (ids is null)
            {
                continue;
            }

            foreach (var id in ids)
            {
                referenced.Add(id);
            }
        }

        return referenced;
    }
}
