using System.Text.RegularExpressions;

namespace WorkLogAI.Core;

public sealed class CandidateMergeService
{
    public IReadOnlyList<ReportCandidate> Merge(IReadOnlyCollection<ReportCandidate> candidates)
    {
        var result = new List<ReportCandidate>();
        foreach (var candidate in candidates.OrderBy(item => item.WorkDate).ThenBy(item => item.Id))
        {
            var index = result.FindIndex(existing => ShouldMerge(existing, candidate));
            if (index < 0)
            {
                result.Add(candidate);
            }
            else
            {
                result[index] = MergePair(result[index], candidate);
            }
        }
        return result;
    }

    private static bool ShouldMerge(ReportCandidate left, ReportCandidate right)
    {
        if (left.SourceEventIds.Intersect(right.SourceEventIds).Any())
        {
            return true;
        }
        if (Math.Abs(left.WorkDate.DayNumber - right.WorkDate.DayNumber) > 1)
        {
            return false;
        }

        var leftText = Normalize($"{left.WorkItem} {left.Activity}");
        var rightText = Normalize($"{right.WorkItem} {right.Activity}");
        return DiceSimilarity(leftText, rightText) >= .72;
    }

    private static ReportCandidate MergePair(ReportCandidate left, ReportCandidate right)
    {
        var evidence = left.SourceEventIds
            .Concat(right.SourceEventIds)
            .Distinct()
            .Order()
            .ToArray();
        var status = ConservativeStatus(left.Status, right.Status);
        return left with
        {
            Id = StableIdentity.CreateGuid($"merged\n{string.Join(",", evidence)}"),
            WorkDate = left.WorkDate <= right.WorkDate ? left.WorkDate : right.WorkDate,
            WorkItem = Longer(left.WorkItem, right.WorkItem),
            Activity = JoinDistinct(left.Activity, right.Activity),
            ResultOrNext = JoinDistinct(left.ResultOrNext, right.ResultOrNext),
            Status = status,
            Confidence = Math.Max(left.Confidence, right.Confidence),
            Selected = left.Selected || right.Selected,
            Edited = left.Edited || right.Edited,
            SourceEventIds = evidence,
            NeedsConfirmation = left.NeedsConfirmation || right.NeedsConfirmation,
            ConfirmationQuestion = JoinDistinct(
                left.ConfirmationQuestion ?? string.Empty,
                right.ConfirmationQuestion ?? string.Empty),
            Origin = left.Origin == CandidateOrigins.Manual || right.Origin == CandidateOrigins.Manual
                ? CandidateOrigins.Manual
                : left.Origin
        };
    }

    private static string ConservativeStatus(string left, string right)
    {
        var order = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["completed"] = 0,
            ["ongoing"] = 1,
            ["pending"] = 2
        };
        return order.GetValueOrDefault(left, 2) >= order.GetValueOrDefault(right, 2)
            ? left
            : right;
    }

    private static string Normalize(string value) =>
        Regex.Replace(value.ToLowerInvariant(), @"[\s\p{P}\p{S}]+", string.Empty);

    private static double DiceSimilarity(string left, string right)
    {
        if (left == right)
        {
            return 1;
        }
        if (left.Length < 2 || right.Length < 2)
        {
            return 0;
        }
        var leftPairs = Enumerable.Range(0, left.Length - 1)
            .Select(index => left.Substring(index, 2)).ToList();
        var rightPairs = Enumerable.Range(0, right.Length - 1)
            .Select(index => right.Substring(index, 2)).ToList();
        var matches = 0;
        foreach (var pair in leftPairs)
        {
            var match = rightPairs.IndexOf(pair);
            if (match >= 0)
            {
                matches++;
                rightPairs.RemoveAt(match);
            }
        }
        return 2d * matches / (leftPairs.Count + rightPairs.Count + matches);
    }

    private static string Longer(string left, string right) =>
        right.Length > left.Length ? right : left;

    private static string JoinDistinct(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left)) return right;
        if (string.IsNullOrWhiteSpace(right) || left.Contains(right, StringComparison.Ordinal)) return left;
        if (right.Contains(left, StringComparison.Ordinal)) return right;
        return $"{left} / {right}";
    }
}

public sealed class CandidateReportMapper
{
    public IReadOnlyList<ReportRow> MapSelected(IEnumerable<ReportCandidate> candidates) =>
        candidates
            .Where(candidate => candidate.Selected)
            .OrderBy(candidate => candidate.WorkDate)
            .ThenBy(candidate => candidate.Id)
            .Select(candidate => new ReportRow(
                candidate.WorkDate,
                candidate.WorkItem,
                candidate.Activity,
                candidate.ResultOrNext))
            .ToArray();
}
