using System.Text;

namespace WorkLogAI.Core;

/// <summary>
/// Pure rewriting of free text into Obsidian-style wikilinks against a known set of
/// entities. No IO, no dictionary lookups — callers build <see cref="EntityLinkTarget"/>
/// selections (see <see cref="EntityLinkTargets.From"/>) ahead of time.
/// </summary>
public static class EntityLinker
{
    /// <summary>
    /// Replaces every occurrence of a target's canonical name or one of its aliases
    /// with <c>[[Canonical]]</c> (or <c>[[Canonical|matched-alias]]</c> when the
    /// matched name differs from the canonical spelling). Matching is ordinal
    /// case-insensitive and requires no word boundary (Japanese text has none), the
    /// longest candidate name wins at each position, a replaced span is never
    /// re-scanned (so replacements never nest), and any text already inside an
    /// existing <c>[[...]]</c> link is left untouched.
    /// </summary>
    public static string Link(string? text, IReadOnlyList<EntityLinkTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (string.IsNullOrEmpty(text) || targets.Count == 0)
        {
            return text ?? string.Empty;
        }

        var candidates = BuildCandidates(targets);
        if (candidates.Count == 0)
        {
            return text;
        }

        var result = new StringBuilder(text.Length);
        var position = 0;
        while (position < text.Length)
        {
            var existingLinkLength = TryMatchExistingLink(text, position);
            if (existingLinkLength > 0)
            {
                result.Append(text, position, existingLinkLength);
                position += existingLinkLength;
                continue;
            }

            var matchedCandidate = FindLongestMatch(text, position, candidates);
            if (matchedCandidate is not null)
            {
                result.Append("[[").Append(matchedCandidate.Value.Canonical);
                if (!string.Equals(matchedCandidate.Value.Name, matchedCandidate.Value.Canonical, StringComparison.Ordinal))
                {
                    result.Append('|').Append(matchedCandidate.Value.Name);
                }
                result.Append("]]");
                position += matchedCandidate.Value.Name.Length;
                continue;
            }

            result.Append(text[position]);
            position++;
        }

        return result.ToString();
    }

    private static List<(string Name, string Canonical)> BuildCandidates(
        IReadOnlyList<EntityLinkTarget> targets)
    {
        var candidates = new List<(string Name, string Canonical)>();
        foreach (var target in targets)
        {
            if (string.IsNullOrEmpty(target.Canonical))
            {
                continue;
            }

            candidates.Add((target.Canonical, target.Canonical));
            foreach (var alias in target.Aliases ?? [])
            {
                if (!string.IsNullOrEmpty(alias)
                    && !alias.Equals(target.Canonical, StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add((alias, target.Canonical));
                }
            }
        }

        // Longest name first so a longer, more specific match (e.g. a part number
        // with a suffix) always wins over a shorter prefix that also happens to be
        // a known name; stable order otherwise preserves declaration order.
        return candidates
            .Select((candidate, index) => (candidate.Name, candidate.Canonical, index))
            .OrderByDescending(item => item.Name.Length)
            .ThenBy(item => item.index)
            .Select(item => (item.Name, item.Canonical))
            .ToList();
    }

    private static (string Name, string Canonical)? FindLongestMatch(
        string text,
        int position,
        List<(string Name, string Canonical)> candidates)
    {
        foreach (var candidate in candidates)
        {
            var name = candidate.Name;
            if (position + name.Length <= text.Length
                && string.Compare(text, position, name, 0, name.Length, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Returns the length of an already-formed <c>[[...]]</c> link starting
    /// at <paramref name="position"/>, or 0 if none starts there.</summary>
    private static int TryMatchExistingLink(string text, int position)
    {
        if (position + 1 >= text.Length || text[position] != '[' || text[position + 1] != '[')
        {
            return 0;
        }

        var close = text.IndexOf("]]", position + 2, StringComparison.Ordinal);
        return close < 0 ? 0 : close + 2 - position;
    }
}
