namespace WorkLogAI.Infrastructure;

/// <summary>
/// Writes a daily note's Markdown to <c>{folder}/yyyy-MM-dd.md</c>, overwriting any
/// existing file at that path — the daily note is a generated file, regenerated in
/// full on every run, never hand-merged. IO failures are thrown to the caller,
/// which is responsible for logging (via ErrorLog, never with the note content
/// itself) and surfacing an error to the user.
/// </summary>
public sealed class DailyNoteWriter
{
    public string Write(string outputFolder, DateOnly date, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFolder);
        ArgumentNullException.ThrowIfNull(content);

        Directory.CreateDirectory(outputFolder);
        var path = Path.Combine(outputFolder, $"{date:yyyy-MM-dd}.md");
        File.WriteAllText(path, content);
        return path;
    }
}

/// <summary>
/// Pure parsing of the vault's <c>entity-exclusions.md</c> file: one entity name
/// per line, optionally wrapped in an Obsidian wikilink (<c>[[Name]]</c>), blank
/// lines and <c>#</c> comment lines skipped. No IO — the caller reads the file and
/// hands its text here, then applies the result via
/// <see cref="WorkLogAI.Core.IEntityRepository.ReplaceExclusionsAsync"/>.
/// </summary>
public static class ExclusionFileParser
{
    private static readonly char[] NewlineChars = ['\r', '\n'];

    public static IReadOnlyList<string> Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var names = new List<string>();
        foreach (var rawLine in content.Split(NewlineChars, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("[[", StringComparison.Ordinal)
                && line.EndsWith("]]", StringComparison.Ordinal)
                && line.Length >= 4)
            {
                line = line[2..^2].Trim();
            }

            if (line.Length > 0)
            {
                names.Add(line);
            }
        }

        return names;
    }
}
