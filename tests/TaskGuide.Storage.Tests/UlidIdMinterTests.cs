using TaskGuide.Domain.Common;
using TaskGuide.Infrastructure.Ids;
using Xunit;

namespace TaskGuide.Storage.Tests;

/// <summary>
/// Against `tests/TEST-INVENTORY.md`'s "Sequential · TaskGuide.Storage.Tests" section: the five
/// remaining prefixed-ULID minters (#23) that later lanes need to mint Windows, Day templates,
/// Patterns and Events.
/// </summary>
public sealed class UlidIdMinterTests
{
    [Fact]
    public void Every_minted_id_carries_its_types_prefix_and_26_crockford_base32_characters()
    {
        var minter = new UlidIdMinter();

        Assert.All(new (string Prefix, string Value)[]
        {
            (WindowId.Prefix, minter.NextWindowId().Value),
            (DayTemplateId.Prefix, minter.NextDayTemplateId().Value),
            (PatternId.Prefix, minter.NextPatternId().Value),
            (EventId.Prefix, minter.NextEventId().Value),
            (EventPrototypeId.Prefix, minter.NextEventPrototypeId().Value),
        }, minted =>
        {
            Assert.StartsWith(minted.Prefix, minted.Value, StringComparison.Ordinal);
            var body = minted.Value[minted.Prefix.Length..];
            Assert.Equal(26, body.Length);
            Assert.All(body, c => Assert.Contains(c, "0123456789ABCDEFGHJKMNPQRSTVWXYZ"));
        });
    }

    [Fact]
    public void Ids_minted_in_sequence_sort_lexicographically_in_mint_order()
    {
        var minter = new UlidIdMinter();

        var minted = new List<string>();
        for (var batch = 0; batch < 50; batch++)
        {
            minted.Add(minter.NextWindowId().Value);
            Thread.Sleep(1);
        }

        var sorted = minted.OrderBy(v => v, StringComparer.Ordinal).ToList();
        Assert.Equal(sorted, minted);
    }

    [Fact]
    public void Two_ids_minted_in_the_same_millisecond_still_differ()
    {
        var minter = new UlidIdMinter();

        var minted = Enumerable.Range(0, 1000).Select(_ => minter.NextWindowId().Value).ToList();

        Assert.Equal(1000, minted.Distinct().Count());
    }

    [Fact]
    public void A_minted_id_is_accepted_by_its_own_iprefixedid_record_struct_round_trip()
    {
        var minter = new UlidIdMinter();

        var windowId = minter.NextWindowId();
        var roundTripped = new WindowId(windowId.Value);

        Assert.Equal(windowId, roundTripped);
    }
}
