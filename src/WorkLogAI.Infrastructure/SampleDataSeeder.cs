using WorkLogAI.Core;

namespace WorkLogAI.Infrastructure;

public sealed class SampleDataSeeder
{
    private readonly IQuickNoteRepository _notes;
    private readonly ISourceEventRepository? _sourceEvents;
    private readonly IReportCandidateRepository? _candidates;

    public SampleDataSeeder(
        IQuickNoteRepository notes,
        ISourceEventRepository? sourceEvents = null,
        IReportCandidateRepository? candidates = null)
    {
        _notes = notes;
        _sourceEvents = sourceEvents;
        _candidates = candidates;
    }

    public async Task SeedIfEmptyAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.Now;
        // Seed inside the current Monday-start week rather than at now-relative
        // offsets: "yesterday" crosses into the previous week when run on a
        // Monday, which made the sample event invisible to current-week queries.
        var week = new WeekRangeCalculator().GetWeekRange(DateOnly.FromDateTime(now.DateTime));
        var weekStartLocal = week.Start.ToDateTime(TimeOnly.MinValue);
        DateTimeOffset AtWeekHour(double hours)
        {
            var local = weekStartLocal.AddHours(hours);
            return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        }

        var existing = await _notes.ListAsync(
            now.AddYears(-10),
            now.AddYears(1),
            includeDeleted: true,
            cancellationToken: cancellationToken);
        if (existing.Count == 0)
        {
            await _notes.CreateAsync("ハイトゲージ教育を継続", AtWeekHour(9), cancellationToken);
            await _notes.CreateAsync("初品5点を検査し全数合格、出荷指示", AtWeekHour(10), cancellationToken);
            await _notes.CreateAsync("検査姿勢改善のため台車を試したが不採用", AtWeekHour(11), cancellationToken);
        }

        if (_sourceEvents is null || _candidates is null)
        {
            return;
        }
        var sample = SourceEventFactory.Create(
            AtWeekHour(12),
            SourceTypes.Git,
            "サンプル: ローカル収集機能を実装",
            "変更ファイル: src/Sample.cs。統計: +12 / -2",
            "sample-mode local metadata",
            "sample:git:phase2",
            0.9);
        await _sourceEvents.InsertIfNewAsync(sample, cancellationToken);

        var existingCandidates = await _candidates.ListAsync(week.Start, cancellationToken);
        if (existingCandidates.Count == 0)
        {
            var weeklyEvents = await _sourceEvents.ListAsync(week, cancellationToken);
            var mapper = new LocalSourceEventMapper();
            await _candidates.ReplaceWeekAsync(
                week.Start,
                weeklyEvents.Select(item => mapper.Map(item, week.Start)).ToArray(),
                cancellationToken);
        }
    }
}
