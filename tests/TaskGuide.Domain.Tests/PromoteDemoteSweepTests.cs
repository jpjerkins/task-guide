using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Tags;
using Xunit;

namespace TaskGuide.Domain.Tests;

/// <summary>
/// `tests/TEST-INVENTORY.md`'s "The promote/demote sweep" section.
/// </summary>
public sealed class PromoteDemoteSweepTests
{
    private static readonly DimensionId Location = new("location");
    private static readonly DimensionId WithWhom = new("withWhom");
    private static readonly DimensionId Energy = new("energy");
    private static readonly DimensionId Priority = new("priority");

    private static CategoricalDimension LocationDimension(params string[] values) =>
        new(Location, "Location", values.Select(v => new TagValue(v)).ToArray());

    private static CategoricalDimension WithWhomDimension(params string[] values) =>
        new(WithWhom, "With whom", values.Select(v => new TagValue(v)).ToArray());

    private static OrdinalDimension EnergyDimension() =>
        new(Energy, "Mental energy", [new("low"), new("medium"), new("high")],
            TaskDefault: new("low"), WindowDefault: new("low"));

    private static TagSet Loose(params string[] values) =>
        new(new Dictionary<DimensionId, IReadOnlyList<TagValue>>(), values.Select(v => new LooseTag(v)).ToArray());

    private static TagSet WithDimensionValue(DimensionId dimension, string value, params string[] looseValues) =>
        new(
            new Dictionary<DimensionId, IReadOnlyList<TagValue>> { [dimension] = [new TagValue(value)] },
            looseValues.Select(v => new LooseTag(v)).ToArray());

    private static void AssertSameTagSet(TagSet expected, TagSet actual)
    {
        var expectedDimensions = expected.Dimensions.Keys.Union(actual.Dimensions.Keys);
        foreach (var dimension in expectedDimensions)
        {
            Assert.Equal(
                expected.On(dimension).Select(v => v.Value).OrderBy(v => v),
                actual.On(dimension).Select(v => v.Value).OrderBy(v => v));
        }

        Assert.Equal(
            expected.LooseTags.Select(t => t.Value).OrderBy(v => v),
            actual.LooseTags.Select(t => t.Value).OrderBy(v => v));
    }

    [Fact]
    public void Declaring_a_Dimension_value_matching_a_loose_Tag_moves_it_into_that_Dimensions_slot_on_every_Task_and_Window_carrying_it()
    {
        var registryWithGarage = new DimensionRegistry([LocationDimension("home", "garage")]);

        var taskTags = Loose("garage", "unrelated");
        var windowTags = Loose("garage");

        var sweptTask = RegistrySweep.Sweep(taskTags, registryWithGarage);
        var sweptWindow = RegistrySweep.Sweep(windowTags, registryWithGarage);

        Assert.Equal(["garage"], sweptTask.On(Location).Select(v => v.Value));
        Assert.Equal(["unrelated"], sweptTask.LooseTags.Select(t => t.Value));

        Assert.Equal(["garage"], sweptWindow.On(Location).Select(v => v.Value));
        Assert.Empty(sweptWindow.LooseTags);
    }

    [Fact]
    public void Withdrawing_a_value_returns_those_Tags_to_the_loose_bag_with_their_strings_intact()
    {
        var registryWithoutGarage = new DimensionRegistry([LocationDimension("home")]);

        // Constructed via the uppercase spelling — TagValue lowercases at construction, so the
        // stored value is already "garage"; the sweep must not mangle it on the way back out.
        var tags = WithDimensionValue(Location, "GARAGE");

        var swept = RegistrySweep.Sweep(tags, registryWithoutGarage);

        Assert.Empty(swept.On(Location));
        Assert.Equal(["garage"], swept.LooseTags.Select(t => t.Value));
    }

    [Fact]
    public void Promote_then_demote_is_lossless_the_round_trip_is_identity()
    {
        var registryBefore = new DimensionRegistry([
            LocationDimension("home"),
            WithWhomDimension("sam"),
            EnergyDimension(),
        ]);

        var original = new TagSet(
            new Dictionary<DimensionId, IReadOnlyList<TagValue>>
            {
                [Location] = [new TagValue("home")],
                [WithWhom] = [new TagValue("sam")],
                [Energy] = [new TagValue("medium")],
            },
            [new LooseTag("urgent"), new LooseTag("someday")]);

        // A Dimension is declared that claims one of the loose Tags.
        var registryWithPriority = new DimensionRegistry([
            LocationDimension("home"),
            WithWhomDimension("sam"),
            EnergyDimension(),
            new CategoricalDimension(Priority, "Priority", [new("urgent")]),
        ]);

        var promoted = RegistrySweep.Sweep(original, registryWithPriority);
        Assert.Equal(["urgent"], promoted.On(Priority).Select(v => v.Value));
        Assert.Equal(["someday"], promoted.LooseTags.Select(t => t.Value));

        // Withdrawing it again must land exactly back where it started.
        var roundTripped = RegistrySweep.Sweep(promoted, registryBefore);

        AssertSameTagSet(original, roundTripped);
    }

    [Fact]
    public void An_ordinal_axis_takes_up_a_loose_Tag_only_if_the_record_has_no_value_on_that_axis()
    {
        var registry = new DimensionRegistry([EnergyDimension()]);
        var tags = Loose("low");

        var swept = RegistrySweep.Sweep(tags, registry);

        Assert.Equal(new TagValue("low"), swept.SingleOn(Energy));
        Assert.Empty(swept.LooseTags);
    }

    [Fact]
    public void A_deliberately_set_ordinal_value_is_never_overruled_by_a_loose_Tag()
    {
        var registry = new DimensionRegistry([EnergyDimension()]);
        var tags = WithDimensionValue(Energy, "medium", "low");

        var swept = RegistrySweep.Sweep(tags, registry);

        Assert.Equal(new TagValue("medium"), swept.SingleOn(Energy));
        Assert.Equal(["low"], swept.LooseTags.Select(t => t.Value));
    }
}
