using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Matching;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using Xunit;

namespace TaskGuide.Domain.Tests;

/// <summary>
/// `tests/TEST-INVENTORY.md`'s "Matching — the two algebras" section.
/// </summary>
public sealed class MatcherTests
{
    private static readonly DimensionRegistry Registry = KnownDimensions.Default;
    private static readonly TaskId Id = new("t_matching");
    private static readonly WindowId WId = new("w_matching");

    private static TagSet Categorical(DimensionId dimension, params string[] values) => new(
        new Dictionary<DimensionId, IReadOnlyList<TagValue>>
        {
            [dimension] = values.Select(v => new TagValue(v)).ToArray(),
        },
        Array.Empty<LooseTag>());

    private static TagSet Merge(params TagSet[] sets)
    {
        var merged = new Dictionary<DimensionId, IReadOnlyList<TagValue>>();
        foreach (var set in sets)
        {
            foreach (var (dimension, values) in set.Dimensions)
            {
                merged[dimension] = merged.TryGetValue(dimension, out var existing)
                    ? existing.Concat(values).ToArray()
                    : values;
            }
        }

        return new TagSet(merged, Array.Empty<LooseTag>());
    }

    private static TagSet Loose(params string[] values) => new(
        new Dictionary<DimensionId, IReadOnlyList<TagValue>>(),
        values.Select(v => new LooseTag(v)).ToArray());

    private static TaskItem Item(TagSet tags) =>
        new(Id, "Do the thing", null, tags, Deadline: null, Defer: null, Postpone: null, Recurrence: null,
            CreatedAt: DateTimeOffset.UtcNow);

    private static AvailabilityWindow Window(TagSet tags) =>
        new(WId, "Some window", new TimeOnly(9, 0), new TimeOnly(10, 0), tags);

    /// <summary>The window-side default Duration ceiling ("30") — irrelevant to every test here.</summary>
    private static MatchContext Context(
        TagSet windowTags,
        IReadOnlyDictionary<DimensionId, IReadOnlyList<TagValue>>? fetched = null) => new(
        Window(windowTags),
        DurationCeiling: new TagValue("30"),
        Fetched: fetched ?? new Dictionary<DimensionId, IReadOnlyList<TagValue>>(),
        FailedFetches: Array.Empty<DimensionId>());

    private static bool FitsWithDuration(TaskItem task, TagSet windowTags, string durationBucket = "30") =>
        Matcher.Fits(
            task with { Tags = Merge(task.Tags, Categorical(KnownDimensions.Duration, durationBucket)) },
            Context(windowTags),
            Registry);

    private static bool FitsWithFetched(
        TaskItem task,
        TagSet windowTags,
        IReadOnlyDictionary<DimensionId, IReadOnlyList<TagValue>> fetched,
        string durationBucket = "30") =>
        Matcher.Fits(
            task with { Tags = Merge(task.Tags, Categorical(KnownDimensions.Duration, durationBucket)) },
            Context(windowTags, fetched),
            Registry);

    [Theory]
    [InlineData(new string[] { }, new string[] { }, true)]
    [InlineData(new string[] { }, new[] { "garage" }, true)]
    [InlineData(new[] { "garage" }, new string[] { }, false)]
    [InlineData(new[] { "garage" }, new[] { "garage", "outside" }, true)]
    [InlineData(new[] { "sam", "ana" }, new[] { "sam" }, false)]
    [InlineData(new[] { "sam", "ana" }, new[] { "sam", "ana", "the kids" }, true)]
    public void Categorical_axis_matches_by_subset(string[] task, string[] window, bool expected)
    {
        var taskValues = task.Select(v => new TagValue(v)).ToArray();
        var windowValues = window.Select(v => new TagValue(v)).ToArray();

        Assert.Equal(expected, Matcher.CategoricalFits(taskValues, windowValues));
    }

    [Fact]
    public void Ordinal_axis_fits_when_task_value_is_at_or_below_the_window_ceiling()
    {
        var energy = (OrdinalDimension)Registry.Dimensions.Single(d => d.Id == KnownDimensions.MentalEnergy);

        Assert.True(Matcher.OrdinalFits(energy, new TagValue("medium"), new TagValue("medium")));
        Assert.True(Matcher.OrdinalFits(energy, new TagValue("low"), new TagValue("high")));
    }

    [Fact]
    public void Ordinal_axis_fails_above_the_ceiling()
    {
        var energy = (OrdinalDimension)Registry.Dimensions.Single(d => d.Id == KnownDimensions.MentalEnergy);

        Assert.False(Matcher.OrdinalFits(energy, new TagValue("high"), new TagValue("low")));
    }

    [Fact]
    public void An_ordinal_axis_silent_on_the_task_side_takes_the_task_side_default()
    {
        var energy = (OrdinalDimension)Registry.Dimensions.Single(d => d.Id == KnownDimensions.MentalEnergy);

        // Task silent -> task-side default "low", which fits any window ceiling.
        Assert.True(Matcher.OrdinalFits(energy, taskValue: null, new TagValue("low")));
        Assert.True(Matcher.OrdinalFits(energy, taskValue: null, new TagValue("high")));
    }

    [Fact]
    public void An_ordinal_axis_silent_on_the_window_side_takes_the_window_side_default()
    {
        var energy = (OrdinalDimension)Registry.Dimensions.Single(d => d.Id == KnownDimensions.MentalEnergy);

        // Window silent -> window-side default "low": a "medium" Task no longer fits.
        Assert.True(Matcher.OrdinalFits(energy, new TagValue("low"), windowValue: null));
        Assert.False(Matcher.OrdinalFits(energy, new TagValue("medium"), windowValue: null));
    }

    [Fact]
    public void A_categorical_axis_has_no_default_on_either_side()
    {
        // Both sides silent still fits (empty subset of empty). A silent Window with a tagged
        // Task fails: no default rescues it, because none exists on a categorical axis.
        var untaggedTask = Item(TagSet.Empty);
        var taggedTask = Item(Categorical(KnownDimensions.Location, "garage"));

        Assert.True(FitsWithDuration(untaggedTask, TagSet.Empty));
        Assert.False(FitsWithDuration(taggedTask, TagSet.Empty));
    }

    [Fact]
    public void Matching_is_a_conjunction_across_axes_failing_one_axis_fails_the_Task()
    {
        // Location fits (garage subset of garage) but With-whom does not (Carrie isn't there).
        var task = Item(Merge(
            Categorical(KnownDimensions.Location, "garage"),
            Categorical(KnownDimensions.WithWhom, "carrie")));
        var window = Categorical(KnownDimensions.Location, "garage");

        Assert.False(FitsWithDuration(task, window));

        // Fix With-whom and both axes now pass.
        var fixedTask = Item(Merge(
            Categorical(KnownDimensions.Location, "garage"),
            Categorical(KnownDimensions.WithWhom, "carrie")));
        var fixedWindow = Merge(
            Categorical(KnownDimensions.Location, "garage"),
            Categorical(KnownDimensions.WithWhom, "carrie"));

        Assert.True(FitsWithDuration(fixedTask, fixedWindow));
    }

    [Fact]
    public void A_rule_reads_only_its_own_axis()
    {
        // The Task fails With-whom but the Window over-satisfies Location; Location's pass
        // must not paper over With-whom's failure, and vice versa below.
        var task = Item(Merge(
            Categorical(KnownDimensions.Location, "garage"),
            Categorical(KnownDimensions.WithWhom, "carrie")));
        var window = Merge(
            Categorical(KnownDimensions.Location, "garage", "outside"),
            Categorical(KnownDimensions.WithWhom, "sam"));

        Assert.False(FitsWithDuration(task, window));
    }

    [Fact]
    public void Loose_Tags_are_ignored_by_matching_on_both_sides()
    {
        // A loose Tag on the Task (belongs to no Dimension) constrains nothing.
        var task = Item(Loose("someday"));
        Assert.True(FitsWithDuration(task, TagSet.Empty));

        // A loose Tag on the Window likewise satisfies nothing — it isn't a declared condition.
        // Using the exact string the Task needs (not a near-miss) is what makes this
        // discriminate: a Matcher that wrongly read the Window's loose bag would pass here.
        var taggedTask = Item(Categorical(KnownDimensions.Location, "garage"));
        Assert.False(FitsWithDuration(taggedTask, Loose("garage")));
    }

    [Fact]
    public void A_fetched_axis_reads_its_Window_side_set_from_Fetched_not_the_Window_s_authored_Tags()
    {
        // Weather is Fetched: its Window-side value never lives on the Window's own Tags.
        var dryTask = Item(Categorical(KnownDimensions.Weather, "dry"));

        var fetchedDry = new Dictionary<DimensionId, IReadOnlyList<TagValue>>
        {
            [KnownDimensions.Weather] = [new TagValue("dry")],
        };
        Assert.True(FitsWithFetched(dryTask, TagSet.Empty, fetchedDry));

        // Unknown/unfetched resolves to the empty set — fails closed, same as absence anywhere
        // else on a categorical axis.
        Assert.False(FitsWithFetched(dryTask, TagSet.Empty, new Dictionary<DimensionId, IReadOnlyList<TagValue>>()));
    }

    [Fact]
    public void A_mistyped_Tag_admits_the_Task_to_more_Windows_not_fewer()
    {
        // "#garge" claims no Dimension value, so it lands in the loose bag: the Location axis
        // reads as silent (∅), which is a subset of every Window — including one with no
        // Location declared at all, which the correctly spelled Tag would have failed.
        var typoTask = Item(Loose("garge"));

        Assert.True(FitsWithDuration(typoTask, TagSet.Empty));
        Assert.True(FitsWithDuration(typoTask, Categorical(KnownDimensions.Location, "garage")));
    }
}
