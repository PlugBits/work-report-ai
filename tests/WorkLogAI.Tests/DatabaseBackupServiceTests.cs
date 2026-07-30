using WorkLogAI.Infrastructure;

namespace WorkLogAI.Tests;

public sealed class DatabaseBackupServiceTests
{
    [Fact]
    public void ShouldBackup_is_true_when_there_are_no_existing_backups()
    {
        Assert.True(DatabaseBackupService.ShouldBackup([], new DateOnly(2026, 7, 30)));
    }

    [Fact]
    public void ShouldBackup_is_false_when_a_fresh_backup_exists()
    {
        var today = new DateOnly(2026, 7, 30);
        var files = new[] { "worklog-20260728.db" };

        Assert.False(DatabaseBackupService.ShouldBackup(files, today));
    }

    [Fact]
    public void ShouldBackup_is_false_for_a_backup_from_today()
    {
        var today = new DateOnly(2026, 7, 30);
        var files = new[] { "worklog-20260730.db" };

        Assert.False(DatabaseBackupService.ShouldBackup(files, today));
    }

    [Fact]
    public void ShouldBackup_is_true_when_the_newest_backup_is_eight_days_old()
    {
        var today = new DateOnly(2026, 7, 30);
        var files = new[] { "worklog-20260722.db" };

        Assert.True(DatabaseBackupService.ShouldBackup(files, today));
    }

    [Fact]
    public void ShouldBackup_ignores_malformed_names()
    {
        var today = new DateOnly(2026, 7, 30);
        var files = new[] { "worklog-notadate.db", "readme.txt" };

        Assert.True(DatabaseBackupService.ShouldBackup(files, today));
    }

    [Fact]
    public void SelectBackupsToDelete_keeps_only_the_newest_four()
    {
        var files = new[]
        {
            "worklog-20260701.db",
            "worklog-20260708.db",
            "worklog-20260715.db",
            "worklog-20260722.db",
            "worklog-20260729.db"
        };

        var toDelete = DatabaseBackupService.SelectBackupsToDelete(files);

        Assert.Equal(new[] { "worklog-20260701.db" }, toDelete);
    }

    [Fact]
    public void SelectBackupsToDelete_keeps_everything_at_or_under_the_retention_count()
    {
        var files = new[] { "worklog-20260701.db", "worklog-20260708.db" };

        var toDelete = DatabaseBackupService.SelectBackupsToDelete(files);

        Assert.Empty(toDelete);
    }

    [Fact]
    public void SelectBackupsToDelete_ignores_malformed_names_when_counting()
    {
        var files = new[]
        {
            "worklog-20260701.db",
            "worklog-20260708.db",
            "worklog-20260715.db",
            "worklog-20260722.db",
            "worklog-20260729.db",
            "not-a-backup.db"
        };

        var toDelete = DatabaseBackupService.SelectBackupsToDelete(files);

        Assert.Equal(new[] { "worklog-20260701.db" }, toDelete);
    }

    [Fact]
    public void RunIfNeeded_does_nothing_when_the_database_file_is_missing()
    {
        using var temp = new TemporaryDirectory();
        var databasePath = System.IO.Path.Combine(temp.Path, "worklog.db");
        var backupsDirectory = System.IO.Path.Combine(temp.Path, "Backups");

        new DatabaseBackupService(databasePath, backupsDirectory).RunIfNeeded();

        Assert.False(Directory.Exists(backupsDirectory));
    }

    [Fact]
    public void RunIfNeeded_copies_the_database_and_applies_retention()
    {
        using var temp = new TemporaryDirectory();
        var databasePath = System.IO.Path.Combine(temp.Path, "worklog.db");
        File.WriteAllText(databasePath, "sample-db-bytes");
        var backupsDirectory = System.IO.Path.Combine(temp.Path, "Backups");
        Directory.CreateDirectory(backupsDirectory);

        var today = DateOnly.FromDateTime(DateTime.Now);
        foreach (var offset in new[] { 40, 30, 20, 10 })
        {
            var name = $"worklog-{today.AddDays(-offset):yyyyMMdd}.db";
            File.WriteAllText(System.IO.Path.Combine(backupsDirectory, name), "old-backup");
        }

        new DatabaseBackupService(databasePath, backupsDirectory).RunIfNeeded();

        var remaining = Directory.GetFiles(backupsDirectory, "worklog-*.db")
            .Select(System.IO.Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var todayFileName = $"worklog-{today:yyyyMMdd}.db";
        Assert.Contains(todayFileName, remaining);
        Assert.Equal(4, remaining.Length);
        Assert.Equal(
            "sample-db-bytes",
            File.ReadAllText(System.IO.Path.Combine(backupsDirectory, todayFileName)));
    }

    [Fact]
    public void RunIfNeeded_skips_a_second_run_on_the_same_day()
    {
        using var temp = new TemporaryDirectory();
        var databasePath = System.IO.Path.Combine(temp.Path, "worklog.db");
        File.WriteAllText(databasePath, "v1");
        var backupsDirectory = System.IO.Path.Combine(temp.Path, "Backups");

        var service = new DatabaseBackupService(databasePath, backupsDirectory);
        service.RunIfNeeded();
        File.WriteAllText(databasePath, "v2");
        service.RunIfNeeded();

        var today = DateOnly.FromDateTime(DateTime.Now);
        var backupPath = System.IO.Path.Combine(backupsDirectory, $"worklog-{today:yyyyMMdd}.db");
        Assert.Equal("v1", File.ReadAllText(backupPath));
    }
}
