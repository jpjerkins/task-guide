using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Tags;
using Xunit;

namespace TaskGuide.Domain.Tests;

/// <summary>
/// `tests/TEST-INVENTORY.md`'s "Dimension registry" section.
/// </summary>
public sealed class DimensionRegistryTests
{
    [Fact]
    public void A_registry_declaring_one_value_on_two_Dimensions_refuses_to_start_naming_the_value()
    {
        var shared = new TagValue("garage");
        var registry = new DimensionRegistry([
            new CategoricalDimension(new DimensionId("location"), "Location", [shared]),
            new CategoricalDimension(new DimensionId("withWhom"), "With whom", [shared]),
        ]);

        var ex = Assert.Throws<DuplicateDimensionValueException>(registry.AssertNoDuplicateValues);

        Assert.Equal(shared.Value, ex.Value);
        Assert.Equal(2, ex.ClaimedBy.Count);
        Assert.Contains(new DimensionId("location"), ex.ClaimedBy);
        Assert.Contains(new DimensionId("withWhom"), ex.ClaimedBy);
    }

    [Fact]
    public void A_duplicate_is_rejected_at_startup_not_resolved_at_the_point_of_use()
    {
        var shared = new TagValue("garage");
        var registry = new DimensionRegistry([
            new CategoricalDimension(new DimensionId("location"), "Location", [shared]),
            new CategoricalDimension(new DimensionId("withWhom"), "With whom", [shared]),
        ]);

        // The defect is caught by asserting the whole registry up front — not by asking,
        // at the moment something claims the value, which Dimension "wins".
        Assert.Throws<DuplicateDimensionValueException>(registry.AssertNoDuplicateValues);
    }

    [Fact]
    public void Identity_and_label_are_independent_renaming_the_label_touches_no_stored_Tag()
    {
        var id = new DimensionId("withWhom");
        var value = new TagValue("sam");
        var original = new CategoricalDimension(id, "With whom", [value]);
        var renamed = original with { Label = "People" };

        var registry = new DimensionRegistry([renamed]);

        Assert.Equal(id, registry.Claiming(value)?.Id);
        Assert.Equal("People", registry.Claiming(value)?.Label);
        // The stored value itself never mentions the label at all.
        Assert.Equal("sam", value.Value);
    }

    [Fact]
    public void A_categorical_Dimension_derives_a_multi_select_control_an_ordinal_one_derives_a_slider()
    {
        var categorical = new CategoricalDimension(new DimensionId("location"), "Location", [new("home")]);
        var ordinal = new OrdinalDimension(new DimensionId("energy"), "Mental energy",
            [new("low"), new("medium"), new("high")],
            TaskDefault: new("low"), WindowDefault: new("low"));

        Assert.IsType<ControlShape.MultiSelect>(categorical.ControlShape);
        Assert.IsType<ControlShape.Slider>(ordinal.ControlShape);
    }

    [Fact]
    public void An_ordinal_Dimension_declaring_a_default_derives_a_leave_at_the_default_control_Duration_declaring_none_derives_no_such_control()
    {
        var withDefault = new OrdinalDimension(new DimensionId("energy"), "Mental energy",
            [new("low"), new("medium"), new("high")],
            TaskDefault: new("low"), WindowDefault: new("low"));
        var duration = new OrdinalDimension(new DimensionId("duration"), "Duration",
            KnownDimensions.DurationBuckets,
            TaskDefault: null, WindowDefault: null,
            WindowSource: WindowValueSource.Derived);

        var withDefaultSlider = Assert.IsType<ControlShape.Slider>(withDefault.ControlShape);
        var durationSlider = Assert.IsType<ControlShape.Slider>(duration.ControlShape);

        Assert.True(withDefaultSlider.HasLeaveAtDefault);
        Assert.False(durationSlider.HasLeaveAtDefault);
    }
}
