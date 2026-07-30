namespace WorkLogAI.Infrastructure;

/// <summary>
/// Best-effort local error log. Never throws. Only context labels and exception
/// type/message/stack trace are written — note bodies, candidate text, mail
/// content, and secrets must never reach this class.
/// </summary>
public static class ErrorLog
{
    private const int RetentionMonths = 3;

    public static void Log(string context, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Log(context, $"{exception.GetType().Name}: {exception.Message}\n{exception.StackTrace}");
    }

    public static void Log(string context, string message)
    {
        try
        {
            var directory = GetLogDirectory();
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"worklog-{DateTime.Now:yyyyMM}.log");
            File.AppendAllText(path, FormatLine(DateTime.Now, context, message) + Environment.NewLine);
            DeleteExpiredLogFiles(directory);
        }
        catch
        {
            // Logging must never throw.
        }
    }

    public static string FormatLine(DateTime timestamp, string context, string message) =>
        $"{timestamp:yyyy-MM-dd HH:mm:ss} [{context}] {message}";

    public static IReadOnlyList<string> SelectExpiredLogFiles(IEnumerable<string> fileNames, DateOnly today)
    {
        var todayIndex = MonthIndex(today.Year, today.Month);
        var result = new List<string>();
        foreach (var fileName in fileNames)
        {
            if (TryParseYearMonth(fileName, out var year, out var month)
                && todayIndex - MonthIndex(year, month) >= RetentionMonths)
            {
                result.Add(fileName);
            }
        }

        return result;
    }

    private static void DeleteExpiredLogFiles(string directory)
    {
        IEnumerable<string> fileNames;
        try
        {
            fileNames = Directory.GetFiles(directory, "worklog-*.log")
                .Select(Path.GetFileName)
                .Where(name => name is not null)
                .Select(name => name!);
        }
        catch
        {
            return;
        }

        foreach (var fileName in SelectExpiredLogFiles(fileNames, DateOnly.FromDateTime(DateTime.Now)))
        {
            try
            {
                File.Delete(Path.Combine(directory, fileName));
            }
            catch
            {
            }
        }
    }

    private static bool TryParseYearMonth(string fileName, out int year, out int month)
    {
        year = 0;
        month = 0;
        var name = Path.GetFileNameWithoutExtension(fileName);
        const string prefix = "worklog-";
        if (!name.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var stamp = name[prefix.Length..];
        if (stamp.Length != 6
            || !int.TryParse(stamp[..4], out year)
            || !int.TryParse(stamp[4..], out month)
            || month is < 1 or > 12)
        {
            return false;
        }

        return true;
    }

    private static int MonthIndex(int year, int month) => (year * 12) + (month - 1);

    private static string GetLogDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WorkLog AI",
        "Logs");
}
