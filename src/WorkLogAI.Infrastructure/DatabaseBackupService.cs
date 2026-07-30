namespace WorkLogAI.Infrastructure;

/// <summary>
/// Weekly, best-effort file-level backup of the production SQLite database. Runs at
/// startup before any connection opens, so a plain file copy is safe. All failures
/// are swallowed and reported to <see cref="ErrorLog"/>; backups never block startup.
/// </summary>
public sealed class DatabaseBackupService(string databasePath, string backupsDirectory)
{
    private const int FreshBackupWindowDays = 7;
    private const int RetainedBackupCount = 4;

    public void RunIfNeeded()
    {
        try
        {
            if (!File.Exists(databasePath))
            {
                return;
            }

            Directory.CreateDirectory(backupsDirectory);
            var today = DateOnly.FromDateTime(DateTime.Now);
            if (ShouldBackup(ListBackupFileNames(), today))
            {
                var fileName = $"worklog-{today:yyyyMMdd}.db";
                File.Copy(databasePath, Path.Combine(backupsDirectory, fileName), overwrite: true);
            }

            foreach (var fileName in SelectBackupsToDelete(ListBackupFileNames()))
            {
                try
                {
                    File.Delete(Path.Combine(backupsDirectory, fileName));
                }
                catch (Exception exception)
                {
                    ErrorLog.Log("DatabaseBackupService", exception);
                }
            }
        }
        catch (Exception exception)
        {
            ErrorLog.Log("DatabaseBackupService", exception);
        }
    }

    public static bool ShouldBackup(IEnumerable<string> existingBackupFileNames, DateOnly today)
    {
        foreach (var fileName in existingBackupFileNames)
        {
            if (TryParseBackupDate(fileName, out var date)
                && today.DayNumber - date.DayNumber < FreshBackupWindowDays)
            {
                return false;
            }
        }

        return true;
    }

    public static IReadOnlyList<string> SelectBackupsToDelete(IEnumerable<string> fileNames)
    {
        var dated = new List<(string Name, DateOnly Date)>();
        foreach (var fileName in fileNames)
        {
            if (TryParseBackupDate(fileName, out var date))
            {
                dated.Add((fileName, date));
            }
        }

        return dated
            .OrderByDescending(item => item.Date)
            .Skip(RetainedBackupCount)
            .Select(item => item.Name)
            .ToArray();
    }

    private IEnumerable<string> ListBackupFileNames()
    {
        try
        {
            return Directory.GetFiles(backupsDirectory, "worklog-*.db")
                .Select(Path.GetFileName)
                .Where(name => name is not null)
                .Select(name => name!)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static bool TryParseBackupDate(string fileName, out DateOnly date)
    {
        date = default;
        var name = Path.GetFileNameWithoutExtension(fileName);
        const string prefix = "worklog-";
        if (!name.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        return DateOnly.TryParseExact(name[prefix.Length..], "yyyyMMdd", out date);
    }
}
