namespace WorkLogAI.Infrastructure;

/// <summary>
/// Pure candidate-filename generation for meeting Markdown exports. Given a base
/// name, extension, and a caller-supplied existence check, produces the first free
/// name — no suffix on first try, then _2, _3, ... on collision. No IO of its own,
/// so it is fully unit-testable with a fake existence predicate.
/// </summary>
public static class MeetingFileNameBuilder
{
    public static string BuildBaseName(DateOnly date, string title) =>
        $"{date:yyyy-MM-dd}_{ReportFileNameSanitizer.Sanitize(title)}";

    public static string NextAvailableFileName(
        string baseName,
        string extension,
        Func<string, bool> fileExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        ArgumentNullException.ThrowIfNull(fileExists);

        var candidate = $"{baseName}{extension}";
        if (!fileExists(candidate))
        {
            return candidate;
        }

        for (var suffix = 2; ; suffix++)
        {
            candidate = $"{baseName}_{suffix}{extension}";
            if (!fileExists(candidate))
            {
                return candidate;
            }
        }
    }
}

/// <summary>
/// Writes meeting Markdown to disk as "{folder}/yyyy-MM-dd_{sanitizedTitle}.md",
/// suffixing "_2", "_3", ... on filename collision. IO failures are thrown to the
/// caller, which is responsible for logging (via ErrorLog, never with the meeting
/// content itself) and surfacing an error to the user.
/// </summary>
public sealed class MeetingMarkdownWriter
{
    public string Write(string outputFolder, DateOnly date, string title, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFolder);
        ArgumentNullException.ThrowIfNull(content);

        Directory.CreateDirectory(outputFolder);
        var baseName = MeetingFileNameBuilder.BuildBaseName(date, title);
        var fileName = MeetingFileNameBuilder.NextAvailableFileName(
            baseName,
            ".md",
            candidate => File.Exists(Path.Combine(outputFolder, candidate)));
        var path = Path.Combine(outputFolder, fileName);
        File.WriteAllText(path, content);
        return path;
    }
}
