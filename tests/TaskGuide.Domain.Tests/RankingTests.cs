using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Ranking;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.Domain.Time;
using Xunit;

namespace TaskGuide.Domain.Tests;

/// <summary>
/// `tests/TEST-INVENTORY.md`'s "Ranking" section.
/// </summary>
public sealed class RankingTests
{
    private static readonly DayBoundary Boundary = new(TimeZoneInfo.FindSystemTimeZoneById(DayBoundary.ZoneId));
    private static readonly ClockTimeResolution Resolution = new(Boundary);
    private static readonly DimensionRegistry Registry = KnownDimensions.Default;

    /// <summary>Tuesday.</summary>
    private static readonly DateOnly Tuesday = new(2026, 9, 1);

    private static readonly DateTimeOffset Noon = Resolution.Resolve(Tuesday, new TimeOnly(12, 0));

    /// <summary>
    /// The Duration axis, read off the registry rather than hard-coded: this test file must pin
    /// the <em>direction</em> of the key, not a bucket list that lives elsewhere.
    /// </summary>
    private static OrdinalDimension DurationAxis => Registry.Dimensions
        .Select(dimension => dimension.Value)
        .OfType<OrdinalDimension>()
        .Single(dimension => dimension.Id == KnownDimensions.Duration);

    /// <summary>
    /// The field is named for the direction it already encodes: negating the ordinal rank makes
    /// an ordinary ascending comparison put the <b>longest</b> Duration first.
    /// </summary>
    private static int DurationKey(string bucket) => -DurationAxis.RankOf(new TagValue(bucket));

    private static RankKey Key(
        UrgencyBand band = UrgencyBand.NoPressure,
        int opportunities = 5,
        string duration = "30",
        int createdDaysAgo = 10) =>
        new(band, opportunities, DurationKey(duration), Noon.AddDays(-createdDaysAgo));

    private static TaskItem Task(string id) => new(
        new TaskId(id),
        id,
        Notes: null,
        Tags: new TagSet(
            new Dictionary<DimensionId, IReadOnlyList<TagValue>>
            {
                [KnownDimensions.Duration] = [new TagValue("30")],
            },
            Array.Empty<LooseTag>()),
        Deadline: null,
        Defer: null,
        Postpone: null,
        Recurrence: null,
        CreatedAt: Noon.AddDays(-10));

    private static IReadOnlyList<string> RankedIds(params (TaskItem Task, RankKey Key)[] eligible) =>
        Ranker.Rank(eligible).Select(task => task.Id.Value).ToList();

    [Fact]
    public void The_sort_is_total_no_two_Tasks_compare_equal_unless_every_key_ties()
    {
        var baseline = Key(UrgencyBand.WithinHorizon, opportunities: 5, duration: "30", createdDaysAgo: 10);

        // Every one of the four keys must be able to break a tie on its own, and only an exact
        // four-way tie may compare equal. A key left out of CompareTo shows up here as a zero.
        Assert.NotEqual(0, baseline.CompareTo(baseline with { Band = UrgencyBand.DeadlinePassed }));
        Assert.NotEqual(0, baseline.CompareTo(baseline with { Opportunities = 4 }));
        Assert.NotEqual(0, baseline.CompareTo(baseline with { DurationRankDescending = DurationKey("60") }));
        Assert.NotEqual(0, baseline.CompareTo(baseline with { CreatedAt = baseline.CreatedAt.AddDays(-1) }));

        Assert.Equal(0, baseline.CompareTo(Key(UrgencyBand.WithinHorizon, 5, "30", 10)));
    }

    [Fact]
    public void Band_1_passed_outranks_band_2_within_horizon_outranks_band_3_no_pressure()
    {
        // Every lower key is stacked against the band order: the passed-Deadline Task has the
        // most Opportunities, the shortest Duration and the newest CreatedAt. Only the band can
        // put it first.
        var order = RankedIds(
            (Task("t_no_pressure"), Key(UrgencyBand.NoPressure, opportunities: 1, duration: "60", createdDaysAgo: 30)),
            (Task("t_passed"), Key(UrgencyBand.DeadlinePassed, opportunities: 9, duration: "2", createdDaysAgo: 1)),
            (Task("t_within"), Key(UrgencyBand.WithinHorizon, opportunities: 5, duration: "30", createdDaysAgo: 15)));

        Assert.Equal(["t_passed", "t_within", "t_no_pressure"], order);
    }

    [Fact]
    public void Within_a_band_fewest_Opportunities_first()
    {
        // Spend the rarest opportunity: the Task only two Windows can take goes before the one
        // eighteen Windows would happily have.
        var order = RankedIds(
            (Task("t_abundant"), Key(UrgencyBand.WithinHorizon, opportunities: 18)),
            (Task("t_scarce"), Key(UrgencyBand.WithinHorizon, opportunities: 2)),
            (Task("t_middling"), Key(UrgencyBand.WithinHorizon, opportunities: 7)));

        Assert.Equal(["t_scarce", "t_middling", "t_abundant"], order);
    }

    [Fact]
    public void On_an_Opportunities_tie_longest_Duration_first()
    {
        // The biggest Task that fits leads. Shortest-first is the exact inversion — the 2-minute
        // Task fits nearly everywhere and will find another slot, so it must come last.
        var order = RankedIds(
            (Task("t_two_minutes"), Key(UrgencyBand.WithinHorizon, opportunities: 4, duration: "2")),
            (Task("t_hour"), Key(UrgencyBand.WithinHorizon, opportunities: 4, duration: "60")),
            (Task("t_ten_minutes"), Key(UrgencyBand.WithinHorizon, opportunities: 4, duration: "10")));

        Assert.Equal(["t_hour", "t_ten_minutes", "t_two_minutes"], order);
    }

    [Fact]
    public void On_a_Duration_tie_oldest_CreatedAt_first()
    {
        // Age is only the backstop, and it runs oldest-first — it is never a penalty, because the
        // Stale gate already encodes that judgement.
        var order = RankedIds(
            (Task("t_newest"), Key(UrgencyBand.WithinHorizon, opportunities: 4, duration: "30", createdDaysAgo: 1)),
            (Task("t_oldest"), Key(UrgencyBand.WithinHorizon, opportunities: 4, duration: "30", createdDaysAgo: 40)),
            (Task("t_middle"), Key(UrgencyBand.WithinHorizon, opportunities: 4, duration: "30", createdDaysAgo: 12)));

        Assert.Equal(["t_oldest", "t_middle", "t_newest"], order);
    }

    [Fact]
    public void A_Task_with_a_Deadline_does_not_automatically_outrank_one_without_bands_not_a_continuous_key()
    {
        // A Deadline a month out is beyond the horizon, so it lands in band 3 alongside a Task
        // that has no Deadline at all — and there loses on Scarcity. A continuous deadline key
        // would have floated it above every undeadlined Task instead, and Scarcity would never
        // get to speak.
        var order = RankedIds(
            (Task("t_deadlined_far_off"), Key(UrgencyBand.NoPressure, opportunities: 12)),
            (Task("t_undeadlined"), Key(UrgencyBand.NoPressure, opportunities: 3)));

        Assert.Equal(["t_undeadlined", "t_deadlined_far_off"], order);
    }

    [Fact]
    public void Inside_band_2_a_sooner_Deadline_yields_a_shorter_horizon_and_therefore_a_higher_rank_without_a_deadline_key_being_applied_on_top()
    {
        // Both Deadlines clip the rolling 7 days, so both Tasks are band 2 and the RankKeys carry
        // no Deadline whatsoever. The counts are the real ones, walked over real dates: the Task
        // due Thursday has two chances left, the one due Sunday has five. The ordering can only
        // have come out of the Opportunities key — there is no deadline comparison on top of it.
        var shapes = new FakeShapes(date => new DayShape(
            date,
            [new AvailabilityWindow(new WindowId("w_morning"), "Morning", new TimeOnly(9, 0), new TimeOnly(10, 0), TagSet.Empty)],
            [],
            IsOverridden: false));
        var counter = new OpportunityCounter(shapes, Registry, Resolution, Boundary);

        var soon = HalfHourTask("t_due_thursday", Tuesday.AddDays(2));
        var later = HalfHourTask("t_due_sunday", Tuesday.AddDays(5));

        Assert.Equal(2, counter.CountAhead(soon, Noon));
        Assert.Equal(5, counter.CountAhead(later, Noon));

        var order = RankedIds(
            (later, Key(UrgencyBand.WithinHorizon, counter.CountAhead(later, Noon))),
            (soon, Key(UrgencyBand.WithinHorizon, counter.CountAhead(soon, Noon))));

        Assert.Equal(["t_due_thursday", "t_due_sunday"], order);
    }

    private static TaskItem HalfHourTask(string id, DateOnly deadline) =>
        Task(id) with { Deadline = deadline };

    /// <summary>The only view of the calendar: a shape per date, and reading one never writes one.</summary>
    private sealed class FakeShapes(Func<DateOnly, DayShape> shapeOf) : IDayShapeReader
    {
        public DayShape For(DateOnly date) => shapeOf(date);
    }
}
