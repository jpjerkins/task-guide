using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Tags;
using Xunit;

namespace TaskGuide.Domain.Tests;

/// <summary>
/// #115: <see cref="TagSet"/> is a positional record whose two members are an
/// <c>IReadOnlyDictionary</c> and an <c>IReadOnlyList</c>, so synthesised equality compares both
/// by reference — two structurally identical <see cref="TagSet"/>s compare unequal unless they
/// are literally the same object. That propagates into <c>TaskItem</c>, <c>AvailabilityWindow</c>
/// and <c>GlanceState</c> (#69/#76/ADR-0011: the Glance floor suppresses a redundant send by
/// comparing states, so every falsely-"changed" tick burns the watch's update budget). Equality
/// here follows the Tag model (`CONTEXT.md` § Tag), not convenience: a Dimension's values are a
/// set (order-insensitive), the loose Tags are a bag (order-insensitive), both are
/// duplicate-count-sensitive, and a Dimension mapped to an empty list equals that Dimension being
/// absent — the same "absence is the empty set" rule <see cref="TagSet.On"/> documents.
/// </summary>
public sealed class TagSetEqualityTests
{
    private static readonly DimensionId Effort = new("effort");
    private static readonly DimensionId Location = new("location");

    [Fact]
    public void Two_separately_constructed_structurally_identical_tag_bearing_sets_compare_equal()
    {
        var a = new TagSet(
            new Dictionary<DimensionId, IReadOnlyList<TagValue>>
            {
                [Effort] = new[] { new TagValue("low") },
            },
            new[] { new LooseTag("errand") });
        var b = new TagSet(
            new Dictionary<DimensionId, IReadOnlyList<TagValue>>
            {
                [Effort] = new[] { new TagValue("low") },
            },
            new[] { new LooseTag("errand") });

        Assert.NotSame(a.Dimensions, b.Dimensions);
        Assert.NotSame(a.LooseTags, b.LooseTags);
        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Two_separately_constructed_empty_sets_compare_equal_and_equal_TagSet_Empty()
    {
        var a = new TagSet(new Dictionary<DimensionId, IReadOnlyList<TagValue>>(), Array.Empty<LooseTag>());
        var b = new TagSet(new Dictionary<DimensionId, IReadOnlyList<TagValue>>(), Array.Empty<LooseTag>());

        Assert.NotSame(a, b);
        Assert.True(a.Equals(b));
        Assert.True(a.Equals(TagSet.Empty));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void A_dimensions_values_compare_equal_regardless_of_order()
    {
        var a = new TagSet(
            new Dictionary<DimensionId, IReadOnlyList<TagValue>>
            {
                [Effort] = new[] { new TagValue("low"), new TagValue("high") },
            },
            Array.Empty<LooseTag>());
        var b = new TagSet(
            new Dictionary<DimensionId, IReadOnlyList<TagValue>>
            {
                [Effort] = new[] { new TagValue("high"), new TagValue("low") },
            },
            Array.Empty<LooseTag>());

        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Loose_tags_compare_equal_regardless_of_order()
    {
        var a = new TagSet(
            new Dictionary<DimensionId, IReadOnlyList<TagValue>>(),
            new[] { new LooseTag("errand"), new LooseTag("outdoors") });
        var b = new TagSet(
            new Dictionary<DimensionId, IReadOnlyList<TagValue>>(),
            new[] { new LooseTag("outdoors"), new LooseTag("errand") });

        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void A_dimension_mapped_to_an_empty_list_equals_that_dimension_being_absent()
    {
        var withEmptyList = new TagSet(
            new Dictionary<DimensionId, IReadOnlyList<TagValue>>
            {
                [Effort] = Array.Empty<TagValue>(),
            },
            Array.Empty<LooseTag>());
        var absent = new TagSet(
            new Dictionary<DimensionId, IReadOnlyList<TagValue>>(),
            Array.Empty<LooseTag>());

        Assert.True(withEmptyList.Equals(absent));
        Assert.Equal(withEmptyList.GetHashCode(), absent.GetHashCode());
    }

    [Fact]
    public void A_different_value_on_a_dimension_compares_unequal()
    {
        var a = new TagSet(
            new Dictionary<DimensionId, IReadOnlyList<TagValue>> { [Effort] = new[] { new TagValue("low") } },
            Array.Empty<LooseTag>());
        var b = new TagSet(
            new Dictionary<DimensionId, IReadOnlyList<TagValue>> { [Effort] = new[] { new TagValue("high") } },
            Array.Empty<LooseTag>());

        Assert.False(a.Equals(b));
    }

    [Fact]
    public void An_extra_dimension_compares_unequal()
    {
        var a = new TagSet(
            new Dictionary<DimensionId, IReadOnlyList<TagValue>> { [Effort] = new[] { new TagValue("low") } },
            Array.Empty<LooseTag>());
        var b = new TagSet(
            new Dictionary<DimensionId, IReadOnlyList<TagValue>>
            {
                [Effort] = new[] { new TagValue("low") },
                [Location] = new[] { new TagValue("home") },
            },
            Array.Empty<LooseTag>());

        Assert.False(a.Equals(b));
    }

    [Fact]
    public void A_different_loose_tag_compares_unequal()
    {
        var a = new TagSet(new Dictionary<DimensionId, IReadOnlyList<TagValue>>(), new[] { new LooseTag("errand") });
        var b = new TagSet(new Dictionary<DimensionId, IReadOnlyList<TagValue>>(), new[] { new LooseTag("outdoors") });

        Assert.False(a.Equals(b));
    }

    [Fact]
    public void A_duplicate_value_compares_unequal_to_the_same_value_once()
    {
        var a = new TagSet(
            new Dictionary<DimensionId, IReadOnlyList<TagValue>>
            {
                [Effort] = new[] { new TagValue("low"), new TagValue("low") },
            },
            Array.Empty<LooseTag>());
        var b = new TagSet(
            new Dictionary<DimensionId, IReadOnlyList<TagValue>>
            {
                [Effort] = new[] { new TagValue("low") },
            },
            Array.Empty<LooseTag>());

        Assert.False(a.Equals(b));
    }

    [Fact]
    public void A_duplicate_loose_tag_compares_unequal_to_the_same_tag_once()
    {
        var a = new TagSet(new Dictionary<DimensionId, IReadOnlyList<TagValue>>(), new[] { new LooseTag("errand"), new LooseTag("errand") });
        var b = new TagSet(new Dictionary<DimensionId, IReadOnlyList<TagValue>>(), new[] { new LooseTag("errand") });

        Assert.False(a.Equals(b));
    }

    [Fact]
    public void Dimension_key_insertion_order_does_not_change_equality_or_the_hash()
    {
        var a = new TagSet(
            new Dictionary<DimensionId, IReadOnlyList<TagValue>>
            {
                [Effort] = new[] { new TagValue("low") },
                [Location] = new[] { new TagValue("home") },
            },
            Array.Empty<LooseTag>());
        var b = new TagSet(
            new Dictionary<DimensionId, IReadOnlyList<TagValue>>
            {
                [Location] = new[] { new TagValue("home") },
                [Effort] = new[] { new TagValue("low") },
            },
            Array.Empty<LooseTag>());

        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    /// <summary>
    /// A hash that sums each Dimension's id hash and its values hash <em>independently</em> lets
    /// one Dimension's id collide with another's values (here, <c>{effort:[x]}</c> vs
    /// <c>{x:[effort]}</c>-shaped pairs via <c>{a:[x], b:[y]}</c> vs <c>{a:[y], b:[x]}</c>) even
    /// though the sets compare unequal — legal under the Equals/GetHashCode contract (unequal
    /// hashes are never required), but an avoidable collision on the Glance comparison path. This
    /// only guards against that specific decomposition, not a general promise that unequal
    /// <see cref="TagSet"/>s hash differently.
    /// </summary>
    [Fact]
    public void Swapping_values_between_two_dimensions_compares_unequal_and_hashes_differently()
    {
        var a = new TagSet(
            new Dictionary<DimensionId, IReadOnlyList<TagValue>>
            {
                [Effort] = new[] { new TagValue("home") },
                [Location] = new[] { new TagValue("low") },
            },
            Array.Empty<LooseTag>());
        var b = new TagSet(
            new Dictionary<DimensionId, IReadOnlyList<TagValue>>
            {
                [Effort] = new[] { new TagValue("low") },
                [Location] = new[] { new TagValue("home") },
            },
            Array.Empty<LooseTag>());

        Assert.False(a.Equals(b));
        Assert.NotEqual(a.GetHashCode(), b.GetHashCode());
    }
}
