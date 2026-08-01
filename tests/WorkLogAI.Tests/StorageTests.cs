using Microsoft.Data.Sqlite;
using WorkLogAI.Core;
using WorkLogAI.Infrastructure;

namespace WorkLogAI.Tests;

public sealed class StorageTests
{
    [Fact]
    public async Task Migration_is_idempotent_and_creates_all_four_tables()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "migration.db");
        var factory = new SqliteConnectionFactory(new FixedDatabasePathProvider(path));
        var initializer = new SqliteDatabaseInitializer(factory);

        await initializer.InitializeAsync();
        await initializer.InitializeAsync();

        await using var connection = factory.Create();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table'
              AND name IN ('quick_notes', 'source_events', 'report_candidates', 'settings')
            ORDER BY name;
            """;
        var tables = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        Assert.Equal(
            new[] { "quick_notes", "report_candidates", "settings", "source_events" },
            tables);

        command.CommandText = "PRAGMA user_version;";
        Assert.Equal(6L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task Migration_upgrades_a_version_one_candidate_table_to_phase_three()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "upgrade.db");
        var factory = new SqliteConnectionFactory(new FixedDatabasePathProvider(path));
        await using (var connection = factory.Create())
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE report_candidates (
                    id TEXT NOT NULL PRIMARY KEY,
                    week_start TEXT NOT NULL,
                    work_date TEXT NOT NULL,
                    work_item TEXT NOT NULL,
                    activity TEXT NOT NULL,
                    result_or_next TEXT NOT NULL,
                    status TEXT NOT NULL,
                    confidence REAL NOT NULL,
                    selected INTEGER NOT NULL,
                    edited INTEGER NOT NULL,
                    source_event_ids_json TEXT NOT NULL
                );
                PRAGMA user_version = 1;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        await new SqliteDatabaseInitializer(factory).InitializeAsync();

        await using var upgraded = factory.Create();
        await upgraded.OpenAsync();
        await using var columns = upgraded.CreateCommand();
        columns.CommandText = "PRAGMA table_info(report_candidates);";
        var names = new List<string>();
        await using (var reader = await columns.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) names.Add(reader.GetString(1));
        }
        Assert.Contains("needs_confirmation", names);
        Assert.Contains("confirmation_question", names);
        Assert.Contains("origin", names);
        Assert.Contains("category", names);
        columns.CommandText = "PRAGMA user_version;";
        Assert.Equal(6L, Convert.ToInt64(await columns.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task Migration_upgrades_a_version_two_database_to_meeting_mode()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "meeting-upgrade.db");
        var factory = new SqliteConnectionFactory(new FixedDatabasePathProvider(path));
        await using (var connection = factory.Create())
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE quick_notes (
                    id TEXT NOT NULL PRIMARY KEY,
                    created_at TEXT NOT NULL,
                    text TEXT NOT NULL,
                    deleted_at TEXT NULL
                );
                CREATE TABLE source_events (
                    id TEXT NOT NULL PRIMARY KEY,
                    occurred_at TEXT NOT NULL,
                    source_type TEXT NOT NULL,
                    title TEXT NOT NULL,
                    body TEXT NOT NULL,
                    evidence TEXT NOT NULL,
                    source_ref TEXT NOT NULL,
                    confidence REAL NOT NULL,
                    content_hash TEXT NOT NULL,
                    collected_at TEXT NOT NULL
                );
                CREATE TABLE report_candidates (
                    id TEXT NOT NULL PRIMARY KEY,
                    week_start TEXT NOT NULL,
                    work_date TEXT NOT NULL,
                    work_item TEXT NOT NULL,
                    activity TEXT NOT NULL,
                    result_or_next TEXT NOT NULL,
                    status TEXT NOT NULL,
                    confidence REAL NOT NULL,
                    selected INTEGER NOT NULL,
                    edited INTEGER NOT NULL,
                    source_event_ids_json TEXT NOT NULL,
                    needs_confirmation INTEGER NOT NULL DEFAULT 0,
                    confirmation_question TEXT NULL,
                    origin TEXT NOT NULL DEFAULT 'local'
                );
                CREATE TABLE settings (
                    key TEXT NOT NULL PRIMARY KEY,
                    value_encrypted TEXT NOT NULL
                );
                PRAGMA user_version = 2;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        await new SqliteDatabaseInitializer(factory).InitializeAsync();

        await using var upgraded = factory.Create();
        await upgraded.OpenAsync();
        await using var tables = upgraded.CreateCommand();
        tables.CommandText = """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table'
              AND name IN ('meeting_sessions', 'meeting_lines', 'meeting_summaries')
            ORDER BY name;
            """;
        var names = new List<string>();
        await using (var reader = await tables.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        }
        Assert.Equal(new[] { "meeting_lines", "meeting_sessions", "meeting_summaries" }, names);

        await using var indexes = upgraded.CreateCommand();
        indexes.CommandText = """
            SELECT name
            FROM sqlite_master
            WHERE type = 'index'
              AND name IN ('ix_meeting_lines_session_line', 'ix_meeting_summaries_session');
            """;
        var indexNames = new List<string>();
        await using (var reader = await indexes.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) indexNames.Add(reader.GetString(0));
        }
        Assert.Equal(2, indexNames.Count);

        await using var version = upgraded.CreateCommand();
        version.CommandText = "PRAGMA user_version;";
        Assert.Equal(6L, Convert.ToInt64(await version.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task Migration_upgrades_a_version_three_database_to_report_category()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "category-upgrade.db");
        var factory = new SqliteConnectionFactory(new FixedDatabasePathProvider(path));
        await using (var connection = factory.Create())
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE quick_notes (
                    id TEXT NOT NULL PRIMARY KEY,
                    created_at TEXT NOT NULL,
                    text TEXT NOT NULL,
                    deleted_at TEXT NULL
                );
                CREATE TABLE source_events (
                    id TEXT NOT NULL PRIMARY KEY,
                    occurred_at TEXT NOT NULL,
                    source_type TEXT NOT NULL,
                    title TEXT NOT NULL,
                    body TEXT NOT NULL,
                    evidence TEXT NOT NULL,
                    source_ref TEXT NOT NULL,
                    confidence REAL NOT NULL,
                    content_hash TEXT NOT NULL,
                    collected_at TEXT NOT NULL
                );
                CREATE TABLE report_candidates (
                    id TEXT NOT NULL PRIMARY KEY,
                    week_start TEXT NOT NULL,
                    work_date TEXT NOT NULL,
                    work_item TEXT NOT NULL,
                    activity TEXT NOT NULL,
                    result_or_next TEXT NOT NULL,
                    status TEXT NOT NULL,
                    confidence REAL NOT NULL,
                    selected INTEGER NOT NULL,
                    edited INTEGER NOT NULL,
                    source_event_ids_json TEXT NOT NULL,
                    needs_confirmation INTEGER NOT NULL DEFAULT 0,
                    confirmation_question TEXT NULL,
                    origin TEXT NOT NULL DEFAULT 'local'
                );
                CREATE TABLE settings (
                    key TEXT NOT NULL PRIMARY KEY,
                    value_encrypted TEXT NOT NULL
                );
                CREATE TABLE meeting_sessions (
                    id TEXT NOT NULL PRIMARY KEY,
                    title TEXT NOT NULL,
                    participants TEXT NOT NULL DEFAULT '',
                    kind TEXT NOT NULL,
                    started_at TEXT NOT NULL,
                    ended_at TEXT NULL,
                    status TEXT NOT NULL,
                    created_at TEXT NOT NULL
                );
                CREATE TABLE meeting_lines (
                    id TEXT NOT NULL PRIMARY KEY,
                    session_id TEXT NOT NULL REFERENCES meeting_sessions(id),
                    line_no INTEGER NOT NULL,
                    marker TEXT NOT NULL,
                    text TEXT NOT NULL,
                    logged_at TEXT NOT NULL
                );
                CREATE TABLE meeting_summaries (
                    id TEXT NOT NULL PRIMARY KEY,
                    session_id TEXT NOT NULL REFERENCES meeting_sessions(id),
                    formatted_json TEXT NOT NULL,
                    summary_line TEXT NOT NULL,
                    created_at TEXT NOT NULL
                );
                PRAGMA user_version = 3;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        await new SqliteDatabaseInitializer(factory).InitializeAsync();

        await using var upgraded = factory.Create();
        await upgraded.OpenAsync();
        await using var columns = upgraded.CreateCommand();
        columns.CommandText = "PRAGMA table_info(report_candidates);";
        var names = new List<string>();
        await using (var reader = await columns.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) names.Add(reader.GetString(1));
        }
        Assert.Contains("category", names);

        await using var version = upgraded.CreateCommand();
        version.CommandText = "PRAGMA user_version;";
        Assert.Equal(6L, Convert.ToInt64(await version.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task Migration_upgrades_a_version_four_database_to_suppressed_source_refs()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "suppression-upgrade.db");
        var factory = new SqliteConnectionFactory(new FixedDatabasePathProvider(path));
        await using (var connection = factory.Create())
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE quick_notes (
                    id TEXT NOT NULL PRIMARY KEY,
                    created_at TEXT NOT NULL,
                    text TEXT NOT NULL,
                    deleted_at TEXT NULL
                );
                CREATE TABLE source_events (
                    id TEXT NOT NULL PRIMARY KEY,
                    occurred_at TEXT NOT NULL,
                    source_type TEXT NOT NULL,
                    title TEXT NOT NULL,
                    body TEXT NOT NULL,
                    evidence TEXT NOT NULL,
                    source_ref TEXT NOT NULL,
                    confidence REAL NOT NULL,
                    content_hash TEXT NOT NULL,
                    collected_at TEXT NOT NULL
                );
                CREATE TABLE report_candidates (
                    id TEXT NOT NULL PRIMARY KEY,
                    week_start TEXT NOT NULL,
                    work_date TEXT NOT NULL,
                    work_item TEXT NOT NULL,
                    activity TEXT NOT NULL,
                    result_or_next TEXT NOT NULL,
                    status TEXT NOT NULL,
                    confidence REAL NOT NULL,
                    selected INTEGER NOT NULL,
                    edited INTEGER NOT NULL,
                    source_event_ids_json TEXT NOT NULL,
                    needs_confirmation INTEGER NOT NULL DEFAULT 0,
                    confirmation_question TEXT NULL,
                    origin TEXT NOT NULL DEFAULT 'local',
                    category TEXT NOT NULL DEFAULT 'internal'
                );
                CREATE TABLE settings (
                    key TEXT NOT NULL PRIMARY KEY,
                    value_encrypted TEXT NOT NULL
                );
                CREATE TABLE meeting_sessions (
                    id TEXT NOT NULL PRIMARY KEY,
                    title TEXT NOT NULL,
                    participants TEXT NOT NULL DEFAULT '',
                    kind TEXT NOT NULL,
                    started_at TEXT NOT NULL,
                    ended_at TEXT NULL,
                    status TEXT NOT NULL,
                    created_at TEXT NOT NULL
                );
                CREATE TABLE meeting_lines (
                    id TEXT NOT NULL PRIMARY KEY,
                    session_id TEXT NOT NULL REFERENCES meeting_sessions(id),
                    line_no INTEGER NOT NULL,
                    marker TEXT NOT NULL,
                    text TEXT NOT NULL,
                    logged_at TEXT NOT NULL
                );
                CREATE TABLE meeting_summaries (
                    id TEXT NOT NULL PRIMARY KEY,
                    session_id TEXT NOT NULL REFERENCES meeting_sessions(id),
                    formatted_json TEXT NOT NULL,
                    summary_line TEXT NOT NULL,
                    created_at TEXT NOT NULL
                );
                PRAGMA user_version = 4;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        await new SqliteDatabaseInitializer(factory).InitializeAsync();

        await using var upgraded = factory.Create();
        await upgraded.OpenAsync();
        await using var tables = upgraded.CreateCommand();
        tables.CommandText = """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table'
              AND name = 'suppressed_source_refs';
            """;
        Assert.Equal("suppressed_source_refs", await tables.ExecuteScalarAsync());

        await using var columns = upgraded.CreateCommand();
        columns.CommandText = "PRAGMA table_info(suppressed_source_refs);";
        var columnNames = new List<string>();
        await using (var reader = await columns.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) columnNames.Add(reader.GetString(1));
        }
        Assert.Equal(new[] { "source_ref", "suppressed_at" }, columnNames);

        await using var version = upgraded.CreateCommand();
        version.CommandText = "PRAGMA user_version;";
        Assert.Equal(6L, Convert.ToInt64(await version.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task Migration_upgrades_a_version_five_database_to_entities()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "entities-upgrade.db");
        var factory = new SqliteConnectionFactory(new FixedDatabasePathProvider(path));
        await using (var connection = factory.Create())
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE quick_notes (
                    id TEXT NOT NULL PRIMARY KEY,
                    created_at TEXT NOT NULL,
                    text TEXT NOT NULL,
                    deleted_at TEXT NULL
                );
                CREATE TABLE source_events (
                    id TEXT NOT NULL PRIMARY KEY,
                    occurred_at TEXT NOT NULL,
                    source_type TEXT NOT NULL,
                    title TEXT NOT NULL,
                    body TEXT NOT NULL,
                    evidence TEXT NOT NULL,
                    source_ref TEXT NOT NULL,
                    confidence REAL NOT NULL,
                    content_hash TEXT NOT NULL,
                    collected_at TEXT NOT NULL
                );
                CREATE TABLE report_candidates (
                    id TEXT NOT NULL PRIMARY KEY,
                    week_start TEXT NOT NULL,
                    work_date TEXT NOT NULL,
                    work_item TEXT NOT NULL,
                    activity TEXT NOT NULL,
                    result_or_next TEXT NOT NULL,
                    status TEXT NOT NULL,
                    confidence REAL NOT NULL,
                    selected INTEGER NOT NULL,
                    edited INTEGER NOT NULL,
                    source_event_ids_json TEXT NOT NULL,
                    needs_confirmation INTEGER NOT NULL DEFAULT 0,
                    confirmation_question TEXT NULL,
                    origin TEXT NOT NULL DEFAULT 'local',
                    category TEXT NOT NULL DEFAULT 'internal'
                );
                CREATE TABLE settings (
                    key TEXT NOT NULL PRIMARY KEY,
                    value_encrypted TEXT NOT NULL
                );
                CREATE TABLE meeting_sessions (
                    id TEXT NOT NULL PRIMARY KEY,
                    title TEXT NOT NULL,
                    participants TEXT NOT NULL DEFAULT '',
                    kind TEXT NOT NULL,
                    started_at TEXT NOT NULL,
                    ended_at TEXT NULL,
                    status TEXT NOT NULL,
                    created_at TEXT NOT NULL
                );
                CREATE TABLE meeting_lines (
                    id TEXT NOT NULL PRIMARY KEY,
                    session_id TEXT NOT NULL REFERENCES meeting_sessions(id),
                    line_no INTEGER NOT NULL,
                    marker TEXT NOT NULL,
                    text TEXT NOT NULL,
                    logged_at TEXT NOT NULL
                );
                CREATE TABLE meeting_summaries (
                    id TEXT NOT NULL PRIMARY KEY,
                    session_id TEXT NOT NULL REFERENCES meeting_sessions(id),
                    formatted_json TEXT NOT NULL,
                    summary_line TEXT NOT NULL,
                    created_at TEXT NOT NULL
                );
                CREATE TABLE suppressed_source_refs (
                    source_ref TEXT NOT NULL PRIMARY KEY,
                    suppressed_at TEXT NOT NULL
                );
                PRAGMA user_version = 5;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        await new SqliteDatabaseInitializer(factory).InitializeAsync();

        await using var upgraded = factory.Create();
        await upgraded.OpenAsync();
        await using var tables = upgraded.CreateCommand();
        tables.CommandText = """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table'
              AND name IN ('entities', 'entity_aliases')
            ORDER BY name;
            """;
        var names = new List<string>();
        await using (var reader = await tables.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        }
        Assert.Equal(new[] { "entities", "entity_aliases" }, names);

        await using var columns = upgraded.CreateCommand();
        columns.CommandText = "PRAGMA table_info(entities);";
        var columnNames = new List<string>();
        await using (var reader = await columns.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) columnNames.Add(reader.GetString(1));
        }
        Assert.Equal(
            new[]
            {
                "id", "canonical_name", "kind", "occurrence_count", "first_seen_at",
                "last_seen_at", "excluded", "created_at"
            },
            columnNames);

        await using var index = upgraded.CreateCommand();
        index.CommandText = """
            SELECT name
            FROM sqlite_master
            WHERE type = 'index'
              AND name = 'ix_entity_aliases_entity_id';
            """;
        Assert.Equal("ix_entity_aliases_entity_id", await index.ExecuteScalarAsync());

        await using var version = upgraded.CreateCommand();
        version.CommandText = "PRAGMA user_version;";
        Assert.Equal(6L, Convert.ToInt64(await version.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task Notes_survive_repository_reopen_and_support_soft_delete_and_reopen()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "notes.db");
        var provider = new FixedDatabasePathProvider(path);
        var factory = new SqliteConnectionFactory(provider);
        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        var createdAt = new DateTimeOffset(2026, 7, 30, 9, 15, 0, TimeSpan.FromHours(-4));

        var firstRepository = new SqliteQuickNoteRepository(factory);
        var created = await firstRepository.CreateAsync("inspection complete", createdAt);

        var reopenedRepository = new SqliteQuickNoteRepository(
            new SqliteConnectionFactory(new FixedDatabasePathProvider(path)));
        var active = await reopenedRepository.ListAsync(createdAt.AddDays(-1), createdAt.AddDays(1));
        Assert.Single(active);
        Assert.Equal(created.Id, active[0].Id);
        Assert.Equal("inspection complete", active[0].Text);

        Assert.True(await reopenedRepository.SoftDeleteAsync(created.Id, createdAt.AddHours(1)));
        Assert.Empty(await reopenedRepository.ListAsync(createdAt.AddDays(-1), createdAt.AddDays(1)));
        var withDeleted = await reopenedRepository.ListAsync(
            createdAt.AddDays(-1),
            createdAt.AddDays(1),
            includeDeleted: true);
        Assert.NotNull(Assert.Single(withDeleted).DeletedAt);

        Assert.True(await reopenedRepository.ReopenAsync(created.Id));
        Assert.Null(Assert.Single(await reopenedRepository.ListAsync(
            createdAt.AddDays(-1),
            createdAt.AddDays(1))).DeletedAt);
    }

    [Fact]
    public async Task Notes_update_text_round_trips_rejects_blank_and_reports_unknown_id()
    {
        using var temporary = new TemporaryDirectory();
        var factory = new SqliteConnectionFactory(
            new FixedDatabasePathProvider(Path.Combine(temporary.Path, "notes-update.db")));
        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        var repository = new SqliteQuickNoteRepository(factory);
        var createdAt = new DateTimeOffset(2026, 7, 30, 9, 15, 0, TimeSpan.FromHours(-4));
        var created = await repository.CreateAsync("first draft", createdAt);

        Assert.True(await repository.UpdateTextAsync(created.Id, "revised text"));
        var updated = Assert.Single(await repository.ListAsync(
            createdAt.AddDays(-1),
            createdAt.AddDays(1)));
        Assert.Equal("revised text", updated.Text);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);

        Assert.False(await repository.UpdateTextAsync(created.Id, "   "));
        Assert.Equal("revised text", Assert.Single(await repository.ListAsync(
            createdAt.AddDays(-1),
            createdAt.AddDays(1))).Text);

        Assert.False(await repository.UpdateTextAsync(Guid.NewGuid(), "unknown id"));
    }

    [Fact]
    public async Task Settings_store_round_trips_allowed_values_and_rejects_secret_names()
    {
        using var temporary = new TemporaryDirectory();
        var factory = new SqliteConnectionFactory(
            new FixedDatabasePathProvider(Path.Combine(temporary.Path, "settings.db")));
        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        var store = new SqliteSettingsStore(factory);

        await store.SetAsync("profile.company_name", "サンプル株式会社");

        Assert.Equal("サンプル株式会社", await store.GetAsync("profile.company_name"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.SetAsync("openai.api-key", "must-not-be-stored"));
    }

    [Fact]
    public async Task App_settings_round_trip_non_secret_collection_paths()
    {
        using var temporary = new TemporaryDirectory();
        var factory = new SqliteConnectionFactory(
            new FixedDatabasePathProvider(Path.Combine(temporary.Path, "path-settings.db")));
        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        var service = new AppSettingsService(new SqliteSettingsStore(factory));
        var expected = new AppSettingsSnapshot(
            "サンプル株式会社",
            "山田 太郎",
            DayOfWeek.Sunday,
            temporary.Path,
            [@"C:\repos\one", @"C:\repos\two"],
            @"C:\codex\sessions",
            [@"C:\business"],
            "gpt-compatible-custom",
            false);

        await service.SaveAsync(expected);
        var actual = await service.LoadAsync();

        Assert.Equal(expected.LocalRepositoryPaths, actual.LocalRepositoryPaths);
        Assert.Equal(expected.CodexSessionFolder, actual.CodexSessionFolder);
        Assert.Equal(expected.RecentFileFolders, actual.RecentFileFolders);
        Assert.Equal(DayOfWeek.Sunday, actual.WeekStartsOn);
        Assert.Equal("gpt-compatible-custom", actual.OpenAiModel);
        Assert.False(actual.SendPreviewEnabled);
    }

    [Fact]
    public async Task Reminder_preferences_round_trip()
    {
        using var temporary = new TemporaryDirectory();
        var factory = new SqliteConnectionFactory(
            new FixedDatabasePathProvider(Path.Combine(temporary.Path, "reminder-settings.db")));
        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        var service = new AppSettingsService(new SqliteSettingsStore(factory));
        var expected = new AppSettingsSnapshot(
            "サンプル株式会社",
            "山田 太郎",
            DayOfWeek.Monday,
            temporary.Path,
            ReminderEnabled: false,
            ConfiguredReminderTimes: [new TimeOnly(9, 30), new TimeOnly(15, 0)],
            ReminderSmartEnabled: false);

        await service.SaveAsync(expected);
        var actual = await service.LoadAsync();

        Assert.False(actual.ReminderEnabled);
        Assert.Equal([new TimeOnly(9, 30), new TimeOnly(15, 0)], actual.ReminderTimes);
        Assert.False(actual.ReminderSmartEnabled);
    }

    [Fact]
    public async Task Reminder_times_default_to_eleven_and_sixteen_when_unset()
    {
        using var temporary = new TemporaryDirectory();
        var factory = new SqliteConnectionFactory(
            new FixedDatabasePathProvider(Path.Combine(temporary.Path, "reminder-default.db")));
        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        var store = new SqliteSettingsStore(factory);
        var service = new AppSettingsService(store);

        var unset = await service.LoadAsync();
        Assert.Equal([new TimeOnly(11, 0), new TimeOnly(16, 0)], unset.ReminderTimes);
        Assert.True(unset.ReminderEnabled);
        Assert.True(unset.ReminderSmartEnabled);
    }

    [Fact]
    public async Task Reminder_times_tolerant_parse_drops_invalid_entries_sorts_and_dedupes()
    {
        using var temporary = new TemporaryDirectory();
        var factory = new SqliteConnectionFactory(
            new FixedDatabasePathProvider(Path.Combine(temporary.Path, "reminder-tolerant.db")));
        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        var store = new SqliteSettingsStore(factory);
        var service = new AppSettingsService(store);

        await store.SetAsync(AppSettingKeys.ReminderTimes, "16:00, not-a-time 11:00,11:00");
        var actual = await service.LoadAsync();

        Assert.Equal([new TimeOnly(11, 0), new TimeOnly(16, 0)], actual.ReminderTimes);
    }

    [Fact]
    public async Task Reminder_times_falls_back_to_default_when_all_entries_are_invalid_or_blank()
    {
        using var temporary = new TemporaryDirectory();
        var factory = new SqliteConnectionFactory(
            new FixedDatabasePathProvider(Path.Combine(temporary.Path, "reminder-all-invalid.db")));
        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        var store = new SqliteSettingsStore(factory);
        var service = new AppSettingsService(store);

        await store.SetAsync(AppSettingKeys.ReminderTimes, "not-a-time, also-bad");
        var actual = await service.LoadAsync();

        Assert.Equal([new TimeOnly(11, 0), new TimeOnly(16, 0)], actual.ReminderTimes);
    }

    [Fact]
    public async Task Reminder_times_soft_migrates_from_legacy_single_time_when_times_key_absent()
    {
        using var temporary = new TemporaryDirectory();
        var factory = new SqliteConnectionFactory(
            new FixedDatabasePathProvider(Path.Combine(temporary.Path, "reminder-migrate.db")));
        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        var store = new SqliteSettingsStore(factory);
        var service = new AppSettingsService(store);

        // Simulate a pre-existing install that only ever wrote the legacy single time.
        await store.SetAsync(AppSettingKeys.ReminderTime, "09:30");
        var actual = await service.LoadAsync();

        Assert.Equal([new TimeOnly(9, 30)], actual.ReminderTimes);
    }

    [Fact]
    public async Task Reminder_times_key_present_takes_priority_over_legacy_single_time()
    {
        using var temporary = new TemporaryDirectory();
        var factory = new SqliteConnectionFactory(
            new FixedDatabasePathProvider(Path.Combine(temporary.Path, "reminder-priority.db")));
        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        var store = new SqliteSettingsStore(factory);
        var service = new AppSettingsService(store);

        await store.SetAsync(AppSettingKeys.ReminderTime, "09:30");
        await store.SetAsync(AppSettingKeys.ReminderTimes, "12:00");
        var actual = await service.LoadAsync();

        Assert.Equal([new TimeOnly(12, 0)], actual.ReminderTimes);
    }

    [Fact]
    public async Task Reminder_per_slot_last_shown_date_state_round_trips_through_settings_store()
    {
        using var temporary = new TemporaryDirectory();
        var factory = new SqliteConnectionFactory(
            new FixedDatabasePathProvider(Path.Combine(temporary.Path, "reminder-slot-state.db")));
        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        var store = new SqliteSettingsStore(factory);

        await store.SetAsync(AppSettingKeys.ReminderSlotLastShownDate(0), "2026-07-30");
        await store.SetAsync(AppSettingKeys.ReminderSlotLastShownDate(1), "2026-07-29");

        Assert.Equal("2026-07-30", await store.GetAsync(AppSettingKeys.ReminderSlotLastShownDate(0)));
        Assert.Equal("2026-07-29", await store.GetAsync(AppSettingKeys.ReminderSlotLastShownDate(1)));
    }

    [Fact]
    public async Task Graph_settings_round_trip_and_default_to_disabled_common_tenant()
    {
        using var temporary = new TemporaryDirectory();
        var factory = new SqliteConnectionFactory(
            new FixedDatabasePathProvider(Path.Combine(temporary.Path, "graph-settings.db")));
        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        var service = new AppSettingsService(new SqliteSettingsStore(factory));

        var unset = await service.LoadAsync();
        Assert.Equal(string.Empty, unset.GraphClientId);
        Assert.Equal("common", unset.GraphTenantId);
        Assert.False(unset.GraphMailEnabled);
        Assert.False(unset.GraphCalendarEnabled);

        var expected = new AppSettingsSnapshot(
            "サンプル株式会社",
            "山田 太郎",
            DayOfWeek.Monday,
            temporary.Path,
            GraphClientId: "11111111-2222-3333-4444-555555555555",
            GraphTenantId: "contoso.onmicrosoft.com",
            GraphMailEnabled: true,
            GraphCalendarEnabled: true);

        await service.SaveAsync(expected);
        var actual = await service.LoadAsync();

        Assert.Equal(expected.GraphClientId, actual.GraphClientId);
        Assert.Equal(expected.GraphTenantId, actual.GraphTenantId);
        Assert.True(actual.GraphMailEnabled);
        Assert.True(actual.GraphCalendarEnabled);
    }

    [Fact]
    public async Task Graph_setting_keys_are_not_treated_as_secrets()
    {
        using var temporary = new TemporaryDirectory();
        var factory = new SqliteConnectionFactory(
            new FixedDatabasePathProvider(Path.Combine(temporary.Path, "graph-key-policy.db")));
        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        var store = new SqliteSettingsStore(factory);

        await store.SetAsync(AppSettingKeys.GraphClientId, "11111111-2222-3333-4444-555555555555");
        await store.SetAsync(AppSettingKeys.GraphTenantId, "common");
        await store.SetAsync(AppSettingKeys.GraphMailEnabled, "true");
        await store.SetAsync(AppSettingKeys.GraphCalendarEnabled, "false");

        Assert.Equal("common", await store.GetAsync(AppSettingKeys.GraphTenantId));
    }

    [Fact]
    public async Task Report_title_setting_round_trips()
    {
        using var temporary = new TemporaryDirectory();
        var factory = new SqliteConnectionFactory(
            new FixedDatabasePathProvider(Path.Combine(temporary.Path, "report-title.db")));
        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        var service = new AppSettingsService(new SqliteSettingsStore(factory));
        var expected = new AppSettingsSnapshot(
            "サンプル株式会社",
            "山田 太郎",
            DayOfWeek.Monday,
            temporary.Path,
            ReportTitle: "カスタム週報");

        await service.SaveAsync(expected);
        var actual = await service.LoadAsync();

        Assert.Equal("カスタム週報", actual.ReportTitle);
    }

    [Fact]
    public async Task Report_title_falls_back_to_default_when_unset_or_blank()
    {
        using var temporary = new TemporaryDirectory();
        var factory = new SqliteConnectionFactory(
            new FixedDatabasePathProvider(Path.Combine(temporary.Path, "report-title-default.db")));
        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        var store = new SqliteSettingsStore(factory);
        var service = new AppSettingsService(store);

        var unset = await service.LoadAsync();
        Assert.Equal("業務週報", unset.ReportTitle);

        await store.SetAsync(AppSettingKeys.ReportTitle, "   ");
        var blank = await service.LoadAsync();
        Assert.Equal("業務週報", blank.ReportTitle);
    }

    [Fact]
    public async Task Meeting_settings_round_trip_and_default_to_enabled_raw_log_and_hotkey()
    {
        using var temporary = new TemporaryDirectory();
        var factory = new SqliteConnectionFactory(
            new FixedDatabasePathProvider(Path.Combine(temporary.Path, "meeting-settings.db")));
        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        var service = new AppSettingsService(new SqliteSettingsStore(factory));

        var unset = await service.LoadAsync();
        Assert.Equal(string.Empty, unset.MeetingOutputFolder);
        Assert.True(unset.MeetingIncludeRawLog);
        Assert.True(unset.MeetingHotkeyEnabled);

        var expected = new AppSettingsSnapshot(
            "サンプル株式会社",
            "山田 太郎",
            DayOfWeek.Monday,
            temporary.Path,
            MeetingOutputFolder: @"C:\meetings",
            MeetingIncludeRawLog: false,
            MeetingHotkeyEnabled: false);

        await service.SaveAsync(expected);
        var actual = await service.LoadAsync();

        Assert.Equal(@"C:\meetings", actual.MeetingOutputFolder);
        Assert.False(actual.MeetingIncludeRawLog);
        Assert.False(actual.MeetingHotkeyEnabled);
    }

    [Fact]
    public async Task Meeting_setting_keys_are_not_treated_as_secrets()
    {
        using var temporary = new TemporaryDirectory();
        var factory = new SqliteConnectionFactory(
            new FixedDatabasePathProvider(Path.Combine(temporary.Path, "meeting-key-policy.db")));
        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        var store = new SqliteSettingsStore(factory);

        await store.SetAsync(AppSettingKeys.MeetingOutputFolder, @"C:\meetings");
        await store.SetAsync(AppSettingKeys.MeetingIncludeRawLog, "true");
        await store.SetAsync(AppSettingKeys.MeetingHotkeyEnabled, "false");
        await store.SetAsync(AppSettingKeys.MeetingWindowPlacement, "0,0,420,520");

        Assert.Equal(@"C:\meetings", await store.GetAsync(AppSettingKeys.MeetingOutputFolder));
        Assert.Equal("0,0,420,520", await store.GetAsync(AppSettingKeys.MeetingWindowPlacement));
    }

    [Fact]
    public async Task Vault_settings_round_trip_and_default_to_the_feature_being_off()
    {
        using var temporary = new TemporaryDirectory();
        var factory = new SqliteConnectionFactory(
            new FixedDatabasePathProvider(Path.Combine(temporary.Path, "vault-settings.db")));
        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        var service = new AppSettingsService(new SqliteSettingsStore(factory));

        var unset = await service.LoadAsync();
        Assert.Equal(string.Empty, unset.VaultDailyNotesFolder);
        Assert.Equal(2, unset.VaultEntityLinkMinOccurrences);

        var expected = new AppSettingsSnapshot(
            "サンプル株式会社",
            "山田 太郎",
            DayOfWeek.Monday,
            temporary.Path,
            VaultDailyNotesFolder: @"C:\vault\daily",
            VaultEntityLinkMinOccurrences: 5);

        await service.SaveAsync(expected);
        var actual = await service.LoadAsync();

        Assert.Equal(@"C:\vault\daily", actual.VaultDailyNotesFolder);
        Assert.Equal(5, actual.VaultEntityLinkMinOccurrences);
    }

    [Fact]
    public async Task Vault_entity_link_min_occurrences_tolerantly_parses_and_enforces_a_minimum_of_one()
    {
        using var temporary = new TemporaryDirectory();
        var factory = new SqliteConnectionFactory(
            new FixedDatabasePathProvider(Path.Combine(temporary.Path, "vault-tolerant.db")));
        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        var store = new SqliteSettingsStore(factory);
        var service = new AppSettingsService(store);

        await store.SetAsync(AppSettingKeys.VaultEntityLinkMinOccurrences, "not-a-number");
        Assert.Equal(2, (await service.LoadAsync()).VaultEntityLinkMinOccurrences);

        await store.SetAsync(AppSettingKeys.VaultEntityLinkMinOccurrences, "0");
        Assert.Equal(2, (await service.LoadAsync()).VaultEntityLinkMinOccurrences);

        await store.SetAsync(AppSettingKeys.VaultEntityLinkMinOccurrences, "-3");
        Assert.Equal(2, (await service.LoadAsync()).VaultEntityLinkMinOccurrences);

        await store.SetAsync(AppSettingKeys.VaultEntityLinkMinOccurrences, "1");
        Assert.Equal(1, (await service.LoadAsync()).VaultEntityLinkMinOccurrences);
    }

    [Fact]
    public async Task Vault_setting_keys_are_not_treated_as_secrets()
    {
        using var temporary = new TemporaryDirectory();
        var factory = new SqliteConnectionFactory(
            new FixedDatabasePathProvider(Path.Combine(temporary.Path, "vault-key-policy.db")));
        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        var store = new SqliteSettingsStore(factory);

        await store.SetAsync(AppSettingKeys.VaultDailyNotesFolder, @"C:\vault\daily");
        await store.SetAsync(AppSettingKeys.VaultEntityLinkMinOccurrences, "3");

        Assert.Equal(@"C:\vault\daily", await store.GetAsync(AppSettingKeys.VaultDailyNotesFolder));
        Assert.Equal("3", await store.GetAsync(AppSettingKeys.VaultEntityLinkMinOccurrences));
    }

    [Fact]
    public void Default_database_path_is_injectable_and_sample_mode_is_isolated()
    {
        const string root = @"C:\tmp\WorkLogAIPathTest";
        var production = new DefaultDatabasePathProvider(root).GetDatabasePath();
        var sample = new DefaultDatabasePathProvider(root, sampleDataMode: true).GetDatabasePath();

        Assert.StartsWith(root, production, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("Data", "worklog.db"), production);
        Assert.EndsWith(Path.Combine("SampleData", "worklog-sample.db"), sample);
        Assert.NotEqual(production, sample);
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "WorkLogAI.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
