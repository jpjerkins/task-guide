using System.Reflection;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Notifications;
using TaskGuide.Domain.Ranking;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.Domain.Time;
using Xunit;

namespace TaskGuide.Domain.Tests;

/// <summary>
/// `tests/TEST-INVENTORY.md`'s "Orphan detection" section.
/// </summary>
public sealed class OrphanDetectionTests
{
    private static readonly DayBoundary Boundary = new(TimeZoneInfo.FindSystemTimeZoneById(DayBoundary.ZoneId));
    private static readonly ClockTimeResolution Resolution = new(Boundary);
    private static readonly DimensionRegistry Registry = KnownDimensions.Default;

    /// <summary>Thresholds arrive as a parameter, never as a constant.</summary>
    private static readonly StaleThresholds Thresholds = new(TimeSpan.FromDays(30), ConsecutiveMissedInstances: 3);

    /// <summary>Tuesday.</summary>
    private static readonly DateOnly Tuesday = new(2026, 9, 1);

    private static DateTimeOffset At(DateOnly date, int hour) => Resolution.Resolve(date, new TimeOnly(hour, 0));

    private static readonly DateTimeOffset Now = At(Tuesday, 12);

    private static AvailabilityWindow Evening { get; } = new(
        new WindowId("w_evening"), "Evening", new TimeOnly(18, 0), new TimeOnly(19, 0), TagSet.Empty);

    /// <summary>An hour every evening — long enough for a half-hour Task, and it declares no Location.</summary>
    private static readonly DayTemplate Workday =
        new(new DayTemplateId("dt_workday"), "Workday", [Evening], []);

    private static Pattern EveryDayIs(DayTemplate template) => new(
        new PatternId("p_" + template.Id.Value), template.Name,
        [.. Enumerable.Repeat(template.Id, 7)]);

    private static TagSet Tags(params (DimensionId Dimension, string Value)[] values) => new(
        values.ToDictionary(v => v.Dimension, v => (IReadOnlyList<TagValue>)[new TagValue(v.Value)]),
        Array.Empty<LooseTag>());

    private static readonly TagSet HalfHour = Tags((KnownDimensions.Duration, "30"));

    /// <summary>Half an hour of work, admitted by the evening Window — nothing malformed about it.</summary>
    private static TaskItem Item(
        string id = "t_orphan",
        TagSet? tags = null,
        DateOnly? deadline = null,
        Defer? defer = null,
        DateOnly? postpone = null,
        DerivedProvenance? provenance = null,
        DateTimeOffset? createdAt = null) =>
        new(new TaskId(id), "Sweep the garage", null, tags ?? HalfHour, deadline, defer, postpone, null,
            createdAt ?? At(Tuesday.AddDays(-3), 9))
        {
            Provenance = provenance,
        };

    /// <summary>
    /// The canonical malformed Task: it carries a Tag no Window declares, so no Window in the
    /// active Pattern can ever admit it however long the Window is.
    /// </summary>
    private static TaskItem GarageTask(
        Defer? defer = null,
        DateOnly? postpone = null,
        DerivedProvenance? provenance = null,
        DateTimeOffset? createdAt = null) =>
        Item(
            "t_garage",
            Tags((KnownDimensions.Duration, "30"), (KnownDimensions.Location, "garage")),
            defer: defer,
            postpone: postpone,
            provenance: provenance,
            createdAt: createdAt);

    private sealed class FakeShapes(Func<DateOnly, DayShape> shapeOf) : IDayShapeReader
    {
        public DayShape For(DateOnly date) => shapeOf(date);
    }

    private static FakeShapes EveryDay(params AvailabilityWindow[] windows) =>
        new(date => new DayShape(date, windows, [], IsOverridden: false));

    private static FakeShapes OnWeekday(DayOfWeek weekday, params AvailabilityWindow[] windows) =>
        new(date => new DayShape(date, date.DayOfWeek == weekday ? windows : [], [], IsOverridden: false));

    private static OpportunityCounter CounterOver(IDayShapeReader shapes) =>
        new(shapes, Registry, Resolution, Boundary);

    private static int PatternWeekCount(TaskItem task, DayTemplate template) =>
        CounterOver(EveryDay(Evening)).CountInPatternWeek(task, EveryDayIs(template), [template], Tuesday);

    private static Status StatusOf(TaskItem task, DateTimeOffset? now = null) =>
        StatusRules.Of(task, CompletionLog.Empty(task.Id), Registry, Thresholds, now ?? Now, Boundary);

    private static bool Eligible(TaskItem task) =>
        StatusRules.IsEligible(task, CompletionLog.Empty(task.Id), Registry, Thresholds, Now, Boundary);

    [Fact]
    public void Opportunities_0_and_a_Pattern_week_count_of_0_is_an_Orphan()
    {
        var counter = CounterOver(EveryDay(Evening));
        var task = GarageTask();
        var patternWeekCount = PatternWeekCount(task, Workday);

        // Nothing ahead, and nothing in the active Pattern either: no Window declares the
        // Location this Task carries, so none of them could ever admit it. Something is
        // malformed — that is the claim the badge makes.
        Assert.Equal(0, counter.CountAhead(task, Now));
        Assert.Equal(0, patternWeekCount);
        Assert.Equal(Status.Active, StatusOf(task));

        Assert.True(OrphanDetection.IsTaskOrphan(task, Status.Active, patternWeekCount));
        Assert.Equal(ZeroKind.Orphan, OrphanDetection.KindOfZero(task, Status.Active, 0, patternWeekCount));
    }

    [Fact]
    public void Opportunities_0_with_a_non_zero_Pattern_week_count_is_none_in_this_stretch_not_an_Orphan()
    {
        // A week of travel: every real date is overridden away, so there is no Opportunity
        // ahead — but the active Pattern still declares a Window that would admit this Task.
        var travelWeek = CounterOver(new FakeShapes(date => new DayShape(date, [], [], IsOverridden: true)));
        var task = Item();
        var patternWeekCount = PatternWeekCount(task, Workday);

        Assert.Equal(0, travelWeek.CountAhead(task, Now));
        Assert.Equal(7, patternWeekCount);

        // The two zeroes look identical on the surface and mean opposite things. Nothing is
        // wrong with this one; it simply should not be ranked as though this were its big chance.
        Assert.False(OrphanDetection.IsTaskOrphan(task, Status.Active, patternWeekCount));
        Assert.Equal(ZeroKind.NoneInThisStretch, OrphanDetection.KindOfZero(task, Status.Active, 0, patternWeekCount));
    }

    [Fact]
    public void An_Unprocessed_Task_is_never_an_Orphan()
    {
        // No Duration is exactly what `Unprocessed` means, and matching has only two inputs —
        // Tags and Duration — so a Pattern-week count over this Task is computed from a missing
        // operand. Orphan-ness is not false here but undefined.
        var unprocessed = Item(tags: Tags((KnownDimensions.Location, "garage")));
        Assert.Equal(Status.Unprocessed, StatusOf(unprocessed));

        // 0 is what the reflex that treats a missing Duration as matching *nothing* would hand
        // in — the false-Orphan defect ADR-0007 records as already made once. The gate refuses
        // the claim whatever the count says.
        Assert.False(OrphanDetection.IsTaskOrphan(unprocessed, StatusOf(unprocessed), 0));
        Assert.Null(OrphanDetection.KindOfZero(unprocessed, StatusOf(unprocessed), 0, 0));

        // Supply the Duration and the question becomes meaningful — so it is the Status gate,
        // not the shape of this Task, that decided the lines above.
        var processed = unprocessed with { Tags = Tags((KnownDimensions.Duration, "30"), (KnownDimensions.Location, "garage")) };
        Assert.Equal(Status.Active, StatusOf(processed));
        Assert.Equal(0, PatternWeekCount(processed, Workday));
        Assert.True(OrphanDetection.IsTaskOrphan(processed, StatusOf(processed), PatternWeekCount(processed, Workday)));
    }

    [Fact]
    public void A_Stale_Task_is_never_an_Orphan()
    {
        // Computable, but useless: the Task cannot fire regardless, so the badge would name a
        // second reason while the first still stands — and the repair may well be *delete*.
        var stale = GarageTask(createdAt: Now.AddDays(-40));
        Assert.Equal(Status.Stale, StatusOf(stale));

        Assert.False(OrphanDetection.IsTaskOrphan(stale, StatusOf(stale), PatternWeekCount(stale, Workday)));
        Assert.Null(OrphanDetection.KindOfZero(stale, StatusOf(stale), 0, PatternWeekCount(stale, Workday)));

        // The same Task a day short of the threshold is an Orphan, so the age is what decided it.
        var fresh = GarageTask(createdAt: Now.AddDays(-29));
        Assert.Equal(Status.Active, StatusOf(fresh));
        Assert.True(OrphanDetection.IsTaskOrphan(fresh, StatusOf(fresh), PatternWeekCount(fresh, Workday)));
    }

    [Fact]
    public void A_deferred_Task_can_be_an_Orphan()
    {
        // Deferred to November, so it is not eligible and has no Opportunities at all. Orphan
        // detection asks whether any Window could *ever* admit it, so the Defer clock has
        // nothing to say — Status means intent; Defer is a clock fact.
        var deferred = GarageTask(defer: new AbsoluteDefer(Tuesday.AddDays(60)));

        Assert.False(Eligible(deferred));
        Assert.Equal(Status.Active, StatusOf(deferred));
        Assert.True(OrphanDetection.IsTaskOrphan(deferred, StatusOf(deferred), PatternWeekCount(deferred, Workday)));
    }

    [Fact]
    public void A_postponed_Task_can_be_an_Orphan()
    {
        var postponed = GarageTask(postpone: Tuesday.AddDays(40));

        Assert.False(Eligible(postponed));
        Assert.Equal(Status.Active, StatusOf(postponed));
        Assert.True(OrphanDetection.IsTaskOrphan(postponed, StatusOf(postponed), PatternWeekCount(postponed, Workday)));
    }

    [Fact]
    public void A_derived_Task_is_subject_to_orphan_detection()
    {
        // A derived Task is a projection of a rule, and an orphaned one means a badly written
        // rule — which is precisely what the badge is for.
        var derived = GarageTask(provenance: new DerivedProvenance(new RuleId("r_bins"), "evt_bins"));

        Assert.NotNull(derived.Provenance);
        Assert.Equal(Status.Active, StatusOf(derived));
        Assert.True(OrphanDetection.IsTaskOrphan(derived, StatusOf(derived), PatternWeekCount(derived, Workday)));

        // And a well-formed derived Task is not one, so provenance is inert to the question
        // in both directions.
        var fits = Item("t_derived", provenance: derived.Provenance);
        Assert.False(OrphanDetection.IsTaskOrphan(fits, StatusOf(fits), PatternWeekCount(fits, Workday)));
    }

    [Fact]
    public void An_Event_is_never_subject_to_it()
    {
        const BindingFlags Public = BindingFlags.Public | BindingFlags.Static;

        // Events are never matched, so the concept does not apply to them — and that is settled
        // in the type system rather than by a guard clause: nothing on this surface accepts one.
        var parameters = typeof(OrphanDetection).GetMethods(Public).SelectMany(method => method.GetParameters());
        Assert.DoesNotContain(parameters, parameter => parameter.ParameterType == typeof(Event));

        // Nor can one arrive as a TaskItem: they are unrelated types, and Matching's own door
        // is TaskItem-shaped too.
        Assert.False(typeof(TaskItem).IsAssignableFrom(typeof(Event)));
        Assert.DoesNotContain(
            typeof(Matching.Matcher).GetMethods(Public).SelectMany(method => method.GetParameters()),
            parameter => parameter.ParameterType == typeof(Event));
    }

    [Fact]
    public void Orphan_is_never_counted_in_the_process_stale_footer_counts_the_three_are_disjoint()
    {
        var unprocessed = Item("t_unprocessed", tags: TagSet.Empty);
        var stale = Item(
            "t_stale",
            Tags((KnownDimensions.Duration, "30"), (KnownDimensions.Location, "garage")),
            createdAt: Now.AddDays(-40));
        var orphan = GarageTask();
        var ordinary = Item("t_ordinary");
        var tasks = new[] { unprocessed, stale, orphan, ordinary };

        // A Pattern-week count is never computed for an `Unprocessed` Task: matching's two
        // inputs are Tags and Duration, and this one has no Duration, so the count would be
        // taken over a missing operand (ADR-0007). That is the caller's obligation; the Stale
        // Task does carry a Duration, so its count is real and `IsOrphan`'s own gate is what
        // keeps it out of the third pile.
        bool IsOrphan(TaskItem task) =>
            StatusOf(task) is not Status.Unprocessed
            && OrphanDetection.IsTaskOrphan(task, StatusOf(task), PatternWeekCount(task, Workday));

        var footer = new FooterCounts(
            tasks.Count(task => StatusOf(task) == Status.Unprocessed),
            tasks.Count(task => StatusOf(task) == Status.Stale),
            tasks.Count(IsOrphan));

        Assert.Equal(new FooterCounts(1, 1, 1), footer);

        // Disjoint, not merely equal in total: no Task is in two of the three piles, which is
        // what makes "categorically worse than Unprocessed or Stale" a coherent sentence.
        foreach (var task in tasks)
        {
            var piles = new[] { StatusOf(task) == Status.Unprocessed, StatusOf(task) == Status.Stale, IsOrphan(task) };

            Assert.True(piles.Count(inPile => inPile) <= 1, $"{task.Id} is in more than one pile");
        }

        // The Orphan is genuinely one, and the Stale Task genuinely would be but for the gate —
        // so the third count is disjoint by rule, not by luck of the fixtures.
        Assert.True(IsOrphan(orphan));
        Assert.Equal(0, PatternWeekCount(stale, Workday));
    }

    [Fact]
    public void Opportunities_1_gets_no_badge()
    {
        // A near-orphan: exactly one Window in the horizon admits it. Ranking already surfaces
        // that by sorting it near the front, and a second channel would blur unfireable from
        // merely rare.
        var counter = CounterOver(OnWeekday(DayOfWeek.Thursday, Evening));
        var task = Item();
        var patternWeekCount = PatternWeekCount(task, Workday);

        Assert.Equal(1, counter.CountAhead(task, Now));
        Assert.False(OrphanDetection.IsTaskOrphan(task, Status.Active, patternWeekCount));

        // One is not a zero at all, so neither kind of zero is being claimed.
        Assert.Null(OrphanDetection.KindOfZero(task, Status.Active, 1, patternWeekCount));
    }

    [Fact]
    public void A_fetched_axis_never_makes_a_zero_read_as_an_Orphan()
    {
        var counter = CounterOver(EveryDay(Evening));
        var sunny = Item("t_sunny", Tags((KnownDimensions.Duration, "30"), (KnownDimensions.Weather, "sunny")));
        var patternWeekCount = PatternWeekCount(sunny, Workday);

        // Nothing is fetched for a Window days out and unknown fails closed, so this Task has no
        // Opportunities ahead. The Pattern-week count asks a counterfactual question in which a
        // live condition is not a constraint at all — so the zero above is the harmless kind.
        Assert.Equal(0, counter.CountAhead(sunny, Now));
        Assert.Equal(7, patternWeekCount);

        Assert.False(OrphanDetection.IsTaskOrphan(sunny, Status.Active, patternWeekCount));
        Assert.Equal(ZeroKind.NoneInThisStretch, OrphanDetection.KindOfZero(sunny, Status.Active, 0, patternWeekCount));
    }

    [Fact]
    public void An_unknown_Opportunity_count_is_a_third_ZeroKind_not_a_zero_and_not_an_absence()
    {
        // ADR-0004's amendment: a failed Opportunity fetch must not read as 0 (the Scarcity key's
        // floor, which would wrongly lift the Task to the top of its band) and must not read as
        // null either — a genuine absence means the count was never asked for. `null` here stands
        // for "asked, but the fetch failed."
        var task = GarageTask();
        var patternWeekCount = PatternWeekCount(task, Workday);

        Assert.Equal(ZeroKind.Unknown, OrphanDetection.KindOfZero(task, Status.Active, null, patternWeekCount));
    }

    [Fact]
    public void The_Status_gate_still_wins_over_an_unknown_count()
    {
        // A Task the Status gate excludes has no Opportunities value at all, whether or not the
        // fetch that would have produced it failed — so a non-Active Status still reads as a
        // plain absence, never as Unknown.
        var unprocessed = Item(tags: Tags((KnownDimensions.Location, "garage")));
        Assert.Equal(Status.Unprocessed, StatusOf(unprocessed));

        Assert.Null(OrphanDetection.KindOfZero(unprocessed, StatusOf(unprocessed), null, 0));
    }
}
