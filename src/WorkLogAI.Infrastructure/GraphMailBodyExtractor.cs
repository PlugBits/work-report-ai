using System.Text.RegularExpressions;

namespace WorkLogAI.Infrastructure;

/// <summary>
/// Extracts the new-content (non-quoted) portion of an Outlook message body and
/// applies the same redaction/length rules used for other collected sources.
/// </summary>
public static partial class GraphMailBodyExtractor
{
    public static string ExtractNewContent(
        string? body,
        string? contentType,
        int maximumCharacters = 2_000)
    {
        var text = body ?? string.Empty;
        if (string.Equals(contentType, "html", StringComparison.OrdinalIgnoreCase))
        {
            text = StripHtml(text);
        }

        var newContent = TruncateAtQuote(text);
        return SafeTextSanitizer.Sanitize(newContent, maximumCharacters);
    }

    private static string StripHtml(string html)
    {
        var withLineBreaks = HtmlBlockBoundary().Replace(html, "\n");
        var withoutTags = HtmlTag().Replace(withLineBreaks, string.Empty);
        return DecodeEntities(withoutTags);
    }

    private static string DecodeEntities(string value) =>
        value
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase)
            .Replace("&lt;", "<", StringComparison.OrdinalIgnoreCase)
            .Replace("&gt;", ">", StringComparison.OrdinalIgnoreCase)
            .Replace("&quot;", "\"", StringComparison.OrdinalIgnoreCase);

    private static string TruncateAtQuote(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var kept = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            if (IsQuoteMarker(line.Trim()))
            {
                break;
            }
            kept.Add(line);
        }
        return string.Join("\n", kept);
    }

    private static bool IsQuoteMarker(string trimmedLine)
    {
        if (trimmedLine.Length == 0)
        {
            return false;
        }
        if (trimmedLine.Contains("-----Original Message-----", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (trimmedLine.Length >= 10 && trimmedLine.All(character => character == '_'))
        {
            return true;
        }
        if (trimmedLine.StartsWith("From:", StringComparison.OrdinalIgnoreCase)
            || trimmedLine.StartsWith("差出人:", StringComparison.Ordinal))
        {
            return true;
        }
        if (trimmedLine.StartsWith("> ", StringComparison.Ordinal))
        {
            return true;
        }
        return OnWroteLine().IsMatch(trimmedLine);
    }

    [GeneratedRegex("(?i)<(br|/p|/div|/li|/tr)\\s*/?>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlBlockBoundary();

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTag();

    [GeneratedRegex("(?i)^on\\s.+\\swrote:\\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex OnWroteLine();
}
