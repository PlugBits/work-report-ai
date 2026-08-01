using WorkLogAI.Core;
using WorkLogAI.Infrastructure;

namespace WorkLogAI.Tests;

public sealed class EntityRepositoryTests
{
    [Fact]
    public async Task New_observation_inserts_a_brand_new_entity_with_its_aliases()
    {
        using var temporary = new TemporaryDirectory();
        var repository = await CreateRepositoryAsync(temporary, "new-entity.db");
        var observedAt = new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.FromHours(9));

        await repository.UpsertObservationsAsync(
            [new EntityObservation("ACME Corp", ["ACME", "アクメ"], EntityKinds.Customer, 3)],
            observedAt);

        var entity = Assert.Single(await repository.ListAsync());
        Assert.Equal("ACME Corp", entity.CanonicalName);
        Assert.Equal(EntityKinds.Customer, entity.Kind);
        Assert.Equal(3, entity.OccurrenceCount);
        Assert.Equal(observedAt, entity.FirstSeenAt);
        Assert.Equal(observedAt, entity.LastSeenAt);
        Assert.False(entity.Excluded);
        Assert.Equal(["ACME", "アクメ"], entity.Aliases.OrderBy(a => a, StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_later_observation_matching_an_existing_alias_increments_the_original_entity()
    {
        using var temporary = new TemporaryDirectory();
        var repository = await CreateRepositoryAsync(temporary, "match-via-alias.db");
        var firstSeen = new DateTimeOffset(2026, 7, 28, 9, 0, 0, TimeSpan.FromHours(9));
        var laterSeen = new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.FromHours(9));

        await repository.UpsertObservationsAsync(
            [new EntityObservation("ACME Corp", ["ACME"], EntityKinds.Customer, 2)],
            firstSeen);
        // A later batch extracts the short form as the canonical spelling this
        // time — it must resolve to the same entity via the existing alias, not
        // create a second "ACME" entity.
        await repository.UpsertObservationsAsync(
            [new EntityObservation("ACME", [], EntityKinds.Customer, 5)],
            laterSeen);

        var entity = Assert.Single(await repository.ListAsync());
        Assert.Equal("ACME Corp", entity.CanonicalName);
        Assert.Equal(7, entity.OccurrenceCount);
        Assert.Equal(firstSeen, entity.FirstSeenAt);
        Assert.Equal(laterSeen, entity.LastSeenAt);
    }

    [Fact]
    public async Task An_alias_that_already_belongs_to_a_different_entity_is_skipped_first_owner_wins()
    {
        using var temporary = new TemporaryDirectory();
        var repository = await CreateRepositoryAsync(temporary, "alias-collision.db");
        var observedAt = new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.FromHours(9));

        await repository.UpsertObservationsAsync(
            [
                new EntityObservation("顧客A", ["共通名"], EntityKinds.Customer, 1),
                new EntityObservation("顧客B", ["共通名"], EntityKinds.Customer, 1)
            ],
            observedAt);

        var entities = await repository.ListAsync();
        Assert.Equal(2, entities.Count);
        var ownerA = entities.Single(e => e.CanonicalName == "顧客A");
        var ownerB = entities.Single(e => e.CanonicalName == "顧客B");
        Assert.Contains("共通名", ownerA.Aliases);
        Assert.DoesNotContain("共通名", ownerB.Aliases);
    }

    [Fact]
    public async Task Kind_upgrades_from_other_but_never_overwrites_a_specific_kind()
    {
        using var temporary = new TemporaryDirectory();
        var repository = await CreateRepositoryAsync(temporary, "kind-upgrade.db");
        var observedAt = new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.FromHours(9));

        await repository.UpsertObservationsAsync(
            [new EntityObservation("山田太郎", [], EntityKinds.Other, 1)],
            observedAt);
        await repository.UpsertObservationsAsync(
            [new EntityObservation("山田太郎", [], EntityKinds.Person, 1)],
            observedAt.AddDays(1));

        var afterUpgrade = Assert.Single(await repository.ListAsync());
        Assert.Equal(EntityKinds.Person, afterUpgrade.Kind);

        // Once specific, a later "other" (or any different specific kind) must not
        // overwrite it.
        await repository.UpsertObservationsAsync(
            [new EntityObservation("山田太郎", [], EntityKinds.Other, 1)],
            observedAt.AddDays(2));
        await repository.UpsertObservationsAsync(
            [new EntityObservation("山田太郎", [], EntityKinds.System, 1)],
            observedAt.AddDays(3));

        var stillPerson = Assert.Single(await repository.ListAsync());
        Assert.Equal(EntityKinds.Person, stillPerson.Kind);
        Assert.Equal(4, stillPerson.OccurrenceCount);
    }

    [Fact]
    public async Task List_excludes_excluded_entities_by_default_but_can_include_them()
    {
        using var temporary = new TemporaryDirectory();
        var repository = await CreateRepositoryAsync(temporary, "list-excluded.db");
        var observedAt = new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.FromHours(9));
        await repository.UpsertObservationsAsync(
            [
                new EntityObservation("公開案件", [], EntityKinds.Project, 1),
                new EntityObservation("除外案件", [], EntityKinds.Project, 1)
            ],
            observedAt);
        await repository.ReplaceExclusionsAsync(["除外案件"]);

        var visible = await repository.ListAsync();
        var all = await repository.ListAsync(includeExcluded: true);

        Assert.Equal(["公開案件"], visible.Select(e => e.CanonicalName));
        Assert.Equal(2, all.Count);
        Assert.True(all.Single(e => e.CanonicalName == "除外案件").Excluded);
    }

    [Fact]
    public async Task ReplaceExclusionsAsync_is_case_insensitive_and_ignores_unknown_names_and_clears_stale_ones()
    {
        using var temporary = new TemporaryDirectory();
        var repository = await CreateRepositoryAsync(temporary, "replace-exclusions.db");
        var observedAt = new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.FromHours(9));
        await repository.UpsertObservationsAsync(
            [
                new EntityObservation("ACME Corp", [], EntityKinds.Customer, 1),
                new EntityObservation("Beta Inc", [], EntityKinds.Customer, 1)
            ],
            observedAt);

        await repository.ReplaceExclusionsAsync(["acme corp", "存在しない名前"]);
        var afterFirst = await repository.ListAsync(includeExcluded: true);
        Assert.True(afterFirst.Single(e => e.CanonicalName == "ACME Corp").Excluded);
        Assert.False(afterFirst.Single(e => e.CanonicalName == "Beta Inc").Excluded);

        // Re-applying with a different list clears the stale exclusion.
        await repository.ReplaceExclusionsAsync(["Beta Inc"]);
        var afterSecond = await repository.ListAsync(includeExcluded: true);
        Assert.False(afterSecond.Single(e => e.CanonicalName == "ACME Corp").Excluded);
        Assert.True(afterSecond.Single(e => e.CanonicalName == "Beta Inc").Excluded);
    }

    [Fact]
    public async Task Repeated_observations_within_one_batch_and_across_batches_are_transactional_and_additive()
    {
        using var temporary = new TemporaryDirectory();
        var repository = await CreateRepositoryAsync(temporary, "batch.db");
        var observedAt = new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.FromHours(9));

        await repository.UpsertObservationsAsync(
            [
                new EntityObservation("ACME Corp", ["ACME"], EntityKinds.Customer, 2),
                new EntityObservation("ACME", [], EntityKinds.Other, 1)
            ],
            observedAt);

        var entity = Assert.Single(await repository.ListAsync());
        Assert.Equal("ACME Corp", entity.CanonicalName);
        Assert.Equal(3, entity.OccurrenceCount);
    }

    [Fact]
    public async Task Empty_observation_batch_is_a_no_op()
    {
        using var temporary = new TemporaryDirectory();
        var repository = await CreateRepositoryAsync(temporary, "empty-batch.db");

        await repository.UpsertObservationsAsync([], DateTimeOffset.Now);

        Assert.Empty(await repository.ListAsync());
    }

    private static async Task<SqliteEntityRepository> CreateRepositoryAsync(
        TemporaryDirectory temporary,
        string name)
    {
        var factory = new SqliteConnectionFactory(
            new FixedDatabasePathProvider(Path.Combine(temporary.Path, name)));
        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        return new SqliteEntityRepository(factory);
    }
}
