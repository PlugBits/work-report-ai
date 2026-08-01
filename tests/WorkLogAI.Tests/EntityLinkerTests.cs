using WorkLogAI.Core;

namespace WorkLogAI.Tests;

public sealed class EntityLinkerTests
{
    [Fact]
    public void Links_a_plain_canonical_occurrence()
    {
        var targets = new[] { new EntityLinkTarget("ACME Corp", []) };

        var result = EntityLinker.Link("ACME Corpに連絡した", targets);

        Assert.Equal("[[ACME Corp]]に連絡した", result);
    }

    [Fact]
    public void Links_an_alias_in_piped_form_using_the_declared_alias_spelling()
    {
        var targets = new[] { new EntityLinkTarget("ACME Corp", ["ACME"]) };

        var result = EntityLinker.Link("ACMEに連絡した", targets);

        Assert.Equal("[[ACME Corp|ACME]]に連絡した", result);
    }

    [Fact]
    public void Matching_is_ordinal_case_insensitive_but_replacement_keeps_canonical_casing()
    {
        var targets = new[] { new EntityLinkTarget("ACME Corp", []) };

        var result = EntityLinker.Link("acme corpに連絡した", targets);

        Assert.Equal("[[ACME Corp]]に連絡した", result);
    }

    [Fact]
    public void Longest_candidate_wins_over_a_shorter_prefix_that_is_also_known()
    {
        var targets = new[]
        {
            new EntityLinkTarget("K3071", []),
            new EntityLinkTarget("K3071-02163", [])
        };

        var result = EntityLinker.Link("部品K3071-02163を交換", targets);

        Assert.Equal("部品[[K3071-02163]]を交換", result);
    }

    [Fact]
    public void A_replaced_span_is_never_rescanned_so_matches_never_nest()
    {
        // "ABCDEF" — "ABC" matches at position 0 and is consumed whole; nothing
        // inside that replaced span (e.g. a hypothetical "BC" target) can match again.
        var targets = new[]
        {
            new EntityLinkTarget("ABC", []),
            new EntityLinkTarget("BC", [])
        };

        var result = EntityLinker.Link("ABCDEF", targets);

        Assert.Equal("[[ABC]]DEF", result);
    }

    [Fact]
    public void Text_already_inside_an_existing_link_is_left_untouched()
    {
        var targets = new[] { new EntityLinkTarget("ACME Corp", []) };

        var result = EntityLinker.Link("[[ACME Corp]]と[[別件|ACME Corp]]について話した", targets);

        Assert.Equal("[[ACME Corp]]と[[別件|ACME Corp]]について話した", result);
    }

    [Fact]
    public void Entities_below_threshold_or_excluded_are_filtered_out_by_EntityLinkTargets()
    {
        var entities = new[]
        {
            new WorkEntity(Guid.NewGuid(), "常連客", "customer", 5, DateTimeOffset.Now, DateTimeOffset.Now, false, []),
            new WorkEntity(Guid.NewGuid(), "新規客", "customer", 1, DateTimeOffset.Now, DateTimeOffset.Now, false, []),
            new WorkEntity(Guid.NewGuid(), "除外客", "customer", 10, DateTimeOffset.Now, DateTimeOffset.Now, true, [])
        };

        var targets = EntityLinkTargets.From(entities, minOccurrences: 2);

        Assert.Single(targets);
        Assert.Equal("常連客", targets[0].Canonical);
    }

    [Fact]
    public void Empty_targets_leave_text_unchanged()
    {
        var result = EntityLinker.Link("何も置換されない", []);

        Assert.Equal("何も置換されない", result);
    }

    [Fact]
    public void Blank_or_null_text_returns_empty_string()
    {
        var targets = new[] { new EntityLinkTarget("ACME Corp", []) };

        Assert.Equal(string.Empty, EntityLinker.Link(null, targets));
        Assert.Equal(string.Empty, EntityLinker.Link(string.Empty, targets));
    }

    [Fact]
    public void Multiple_distinct_occurrences_in_the_same_text_are_all_linked()
    {
        var targets = new[]
        {
            new EntityLinkTarget("ACME Corp", ["ACME"]),
            new EntityLinkTarget("K3071", [])
        };

        var result = EntityLinker.Link("ACMEからK3071の注文を受けた。ACME Corpにも連絡。", targets);

        Assert.Equal("[[ACME Corp|ACME]]から[[K3071]]の注文を受けた。[[ACME Corp]]にも連絡。", result);
    }
}
