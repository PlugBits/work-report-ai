using WorkLogAI.Core;
using WorkLogAI.Infrastructure;

namespace WorkLogAI.Tests;

public sealed class RecentFileCollectorTests
{
    [Fact]
    public async Task Collector_reads_only_metadata_and_excludes_sensitive_generated_and_old_files()
    {
        using var temporary = new TemporaryDirectory();
        var current = Path.Combine(temporary.Path, "report.txt");
        var secret = Path.Combine(temporary.Path, ".env");
        var credential = Path.Combine(temporary.Path, "service-credentials.json");
        var generatedFolder = Path.Combine(temporary.Path, "bin");
        Directory.CreateDirectory(generatedFolder);
        var generated = Path.Combine(generatedFolder, "generated.dll");
        var old = Path.Combine(temporary.Path, "old.txt");
        await File.WriteAllTextAsync(current, "FILE_BODY_MARKER");
        await File.WriteAllTextAsync(secret, "SECRET_MARKER");
        await File.WriteAllTextAsync(credential, "CREDENTIAL_MARKER");
        await File.WriteAllTextAsync(generated, "GENERATED_MARKER");
        await File.WriteAllTextAsync(old, "OLD_MARKER");
        File.SetLastWriteTime(old, DateTime.Now.AddDays(-30));
        var range = new WeekRangeCalculator().GetWeekRange(DateOnly.FromDateTime(DateTime.Today));

        var result = await new RecentFileCollector([temporary.Path]).CollectAsync(range);

        var item = Assert.Single(result.Events);
        Assert.Equal("report.txt", item.Title);
        Assert.Contains("TXT", item.Body);
        Assert.Contains(current, item.Evidence);
        var allSelected = $"{item.Title} {item.Body} {item.Evidence}";
        Assert.DoesNotContain("FILE_BODY_MARKER", allSelected);
        Assert.DoesNotContain("SECRET_MARKER", allSelected);
        Assert.DoesNotContain("CREDENTIAL_MARKER", allSelected);
        Assert.DoesNotContain("GENERATED_MARKER", allSelected);
    }
}
