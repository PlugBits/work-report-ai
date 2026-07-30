using System.Text.RegularExpressions;

namespace WorkLogAI.Infrastructure;

public static partial class SafeTextSanitizer
{
    public static string Sanitize(string? value, int maximumCharacters = 2_000)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var withoutCode = FencedCode().Replace(value, "[コード省略]");
        var redacted = SecretAssignment().Replace(withoutCode, "$1=[REDACTED]");
        redacted = BearerToken().Replace(redacted, "Bearer [REDACTED]");
        redacted = OpenAiStyleToken().Replace(redacted, "[REDACTED]");

        var safeLines = redacted
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !EnvironmentAssignment().IsMatch(line))
            .Where(line => !SourceCodeLine().IsMatch(line))
            .Select(line => line.Length > 500 ? line[..500] : line);
        var result = string.Join(" ", safeLines).Trim();
        return result.Length <= maximumCharacters ? result : result[..maximumCharacters];
    }

    [GeneratedRegex("```[\\s\\S]*?```", RegexOptions.CultureInvariant)]
    private static partial Regex FencedCode();

    [GeneratedRegex(
        "(?i)\\b(api[_-]?key|token|password|secret|credential|client[_-]?secret)\\b\\s*[:=]\\s*[^\\s,;]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex SecretAssignment();

    [GeneratedRegex("(?i)\\bBearer\\s+[A-Za-z0-9._~+/-]{8,}=*", RegexOptions.CultureInvariant)]
    private static partial Regex BearerToken();

    [GeneratedRegex("\\bsk-[A-Za-z0-9_-]{8,}\\b", RegexOptions.CultureInvariant)]
    private static partial Regex OpenAiStyleToken();

    [GeneratedRegex(
        "(?i)^\\s*[A-Z_][A-Z0-9_]{2,}\\s*=\\s*.+$",
        RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentAssignment();

    [GeneratedRegex(
        "(?i)^\\s*(```|diff\\s+--git|@@|using\\s+[A-Za-z_]|namespace\\s+[A-Za-z_]|" +
        "(public|private|protected|internal)\\s+(sealed\\s+|static\\s+|partial\\s+)*(class|record|struct|interface|enum)\\b|" +
        "<Project\\b|[{}]\\s*$)",
        RegexOptions.CultureInvariant)]
    private static partial Regex SourceCodeLine();
}

public static class SafePathPolicy
{
    private static readonly HashSet<string> SensitiveExtensions = new(
        [".key", ".pem", ".pfx", ".p12", ".cer", ".crt", ".der", ".jks"],
        StringComparer.OrdinalIgnoreCase);

    public static bool IsSensitive(string path)
    {
        var name = Path.GetFileName(path);
        var normalized = name.Replace("-", string.Empty).Replace("_", string.Empty);
        return name.Equals(".env", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(".env.", StringComparison.OrdinalIgnoreCase)
            || SensitiveExtensions.Contains(Path.GetExtension(name))
            || normalized.Contains("credential", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("privatekey", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("auth", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("token", StringComparison.OrdinalIgnoreCase);
    }
}
