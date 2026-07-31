using WorkLogAI.Core;
using WorkLogAI.Infrastructure;

namespace WorkLogAI.Tests;

public sealed class CandidateTextCleanupTests
{
    [Fact]
    public async Task RunAsync_updates_only_activities_containing_the_file_list_marker_and_is_idempotent()
    {
        using var temporary = new TemporaryDirectory();
        var factory = new SqliteConnectionFactory(
            new FixedDatabasePathProvider(System.IO.Path.Combine(temporary.Path, "cleanup.db")));
        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        var repository = new SqliteReportCandidateRepository(factory);

        var weekStart = new DateOnly(2026, 7, 27);
        var workDate = new DateOnly(2026, 7, 30);

        var dirty = new ReportCandidate(
            Guid.NewGuid(),
            weekStart,
            workDate,
            "ローカルGit",
            "subjectA — 変更ファイル: a.md, b.md。統計: +1 / -0 / summaryB / subjectC — "
                + "変更ファイル: c.cs。統計: +2 / -1",
            string.Empty,
            "pending",
            .9,
            false,
            false,
            [Guid.NewGuid()]);
        var editedDirty = dirty with
        {
            Id = Guid.NewGuid(),
            Activity = "件名D — 変更ファイル: d.txt",
            Edited = true,
            Selected = true,
            SourceEventIds = [Guid.NewGuid()]
        };
        var clean = dirty with
        {
            Id = Guid.NewGuid(),
            Activity = "件名E — 統計: +3 / -1",
            SourceEventIds = [Guid.NewGuid()]
        };

        await repository.ReplaceWeekAsync(weekStart, [dirty, editedDirty, clean]);

        var cleanup = new CandidateTextCleanup(repository);
        var updatedCount = await cleanup.RunAsync();

        Assert.Equal(2, updatedCount);

        var afterFirstRun = (await repository.ListAsync(weekStart))
            .ToDictionary(candidate => candidate.Id);

        Assert.Equal(
            "subjectA — 統計: +1 / -0 / summaryB / subjectC — 統計: +2 / -1",
            afterFirstRun[dirty.Id].Activity);
        Assert.DoesNotContain("変更ファイル", afterFirstRun[dirty.Id].Activity);

        // The edited row is cleaned too — edited/selected/origin/every other column
        // stay exactly as stored.
        Assert.Equal("件名D", afterFirstRun[editedDirty.Id].Activity);
        Assert.True(afterFirstRun[editedDirty.Id].Edited);
        Assert.True(afterFirstRun[editedDirty.Id].Selected);
        Assert.Equal(editedDirty.Origin, afterFirstRun[editedDirty.Id].Origin);
        Assert.Equal(editedDirty.SourceEventIds, afterFirstRun[editedDirty.Id].SourceEventIds);

        // The already-clean row is byte-identical to what was stored, and every
        // other column is untouched.
        var cleanAfter = afterFirstRun[clean.Id];
        Assert.Equal(clean.Activity, cleanAfter.Activity);
        Assert.Equal(clean.WorkItem, cleanAfter.WorkItem);
        Assert.Equal(clean.Status, cleanAfter.Status);
        Assert.Equal(clean.Edited, cleanAfter.Edited);
        Assert.Equal(clean.Selected, cleanAfter.Selected);
        Assert.Equal(clean.Origin, cleanAfter.Origin);
        Assert.Equal(clean.SourceEventIds, cleanAfter.SourceEventIds);

        var secondRunUpdatedCount = await cleanup.RunAsync();
        Assert.Equal(0, secondRunUpdatedCount);
    }
}
