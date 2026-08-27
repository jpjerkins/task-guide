using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Matching;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.Domain.Time;
using Xunit;

namespace TaskGuide.Domain.Tests;

/// <summary>
/// `tests/TEST-INVENTORY.md`'s "Snooze arithmetic" section.
/// </summary>
public sealed class SnoozeTests
{
    private static readonly DayBoundary Boundary = new(TimeZoneInfo.FindSystemTimeZoneById(DayBoundary.ZoneId));
    private static readonly ClockTimeResolution Resolution = new(Boundary);
    private static readonly DimensionRegistry Registry = KnownDimensions.Default;
    private static readonly IReadOnlyList<TagValue> Buckets = KnownDimensions.DurationBuckets;
    private static readonly WindowId WId = new("w_snooze");
    private static readonly TaskId TId = new("t_snooze");
    private static readonly DateOnly Date = new(2026, 8, 27); // an ordinary Chicago day, no DST transition

    private static readonly IReadOnlyDictionary<DimensionId, IReadOnlyList<TagValue>> NoFetched =
        new Dictionary<DimensionId, IReadOnlyList<TagValue>>();

    private static AvailabilityWindow Window(TimeOnly start, TimeOnly end, TagSet? tags = null) =>
        new(WId, "Garage", start, end, tags ?? TagSet.Empty);

    private static TimeSpan LengthOf(AvailabilityWindow window) =>
        Resolution.LengthOf(Date, window.Start, window.End);

    /// <summary>The instant of a clock time on <see cref="Date"/> — never a hand-built offset.</summary>
    private static DateTimeOffset At(int hour, int minute) => Resolution.Resolve(Date, new TimeOnly(hour, minute));

    private static TagSet Values(DimensionId dimension, params string[] values) => new(
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

        return new TagSet(merged, sets.SelectMany(s => s.LooseTags).ToArray());
    }

    private static TaskItem Item(TagSet tags) =>
        new(TId, "Do the thing", null, tags, Deadline: null, Defer: null, Postpone: null, Recurrence: null,
            CreatedAt: At(9, 0));

    [Fact]
    public void Interval_is_clamp_of_25_percent_of_length_5_min_30_min()
    {
        // Between the bounds, the proportion is what governs: a 60-minute Window gives 15.
        Assert.Equal(TimeSpan.FromMinutes(15), SnoozePolicy.IntervalFor(TimeSpan.FromMinutes(60)));
        Assert.Equal(TimeSpan.FromMinutes(21), SnoozePolicy.IntervalFor(TimeSpan.FromMinutes(84)));

        // And it is the same number every time — repeats are unlimited, with no escalation.
        var first = SnoozePolicy.IntervalFor(TimeSpan.FromMinutes(60));
        var seventh = SnoozePolicy.IntervalFor(TimeSpan.FromMinutes(60));
        Assert.Equal(first, seventh);
    }

    [Fact]
    public void A_10_minute_Window_floors_at_5_minutes_a_4_hour_Window_caps_at_30()
    {
        // 25% of 10 minutes is 2:30 — floored, so a short Window cannot buzz again almost immediately.
        Assert.Equal(TimeSpan.FromMinutes(5), SnoozePolicy.IntervalFor(TimeSpan.FromMinutes(10)));

        // 25% of 4 hours is 60 minutes — capped.
        Assert.Equal(TimeSpan.FromMinutes(30), SnoozePolicy.IntervalFor(TimeSpan.FromHours(4)));

        // Just inside each bound the clamp must not engage: 20 min → 5, 2 h → 30 are the exact edges.
        Assert.Equal(TimeSpan.FromMinutes(5), SnoozePolicy.IntervalFor(TimeSpan.FromMinutes(20)));
        Assert.Equal(TimeSpan.FromMinutes(6), SnoozePolicy.IntervalFor(TimeSpan.FromMinutes(24)));
        Assert.Equal(TimeSpan.FromMinutes(30), SnoozePolicy.IntervalFor(TimeSpan.FromHours(2)));
        Assert.Equal(TimeSpan.FromMinutes(29), SnoozePolicy.IntervalFor(TimeSpan.FromMinutes(116)));
    }

    [Fact]
    public void Offered_iff_now_plus_interval_is_before_the_Reminders_Day_boundary()
    {
        var midnight = Boundary.EndOf(Date);
        var interval = SnoozePolicy.IntervalFor(TimeSpan.FromMinutes(60)); // 15 minutes

        Assert.True(SnoozePolicy.IsOffered(At(20, 0), interval, midnight));

        // The biconditional's other half: land past the boundary and it is refused.
        Assert.False(SnoozePolicy.IsOffered(At(23, 50), interval, midnight));

        // Landing exactly on the boundary is not "inside the Reminder's own day" — `<`, not `<=`.
        Assert.False(SnoozePolicy.IsOffered(midnight - interval, interval, midnight));
        Assert.True(SnoozePolicy.IsOffered(midnight - interval - TimeSpan.FromMinutes(1), interval, midnight));
    }

    [Fact]
    public void A_Window_firing_at_11_50p_offers_no_Snooze()
    {
        var window = Window(new TimeOnly(23, 50), new TimeOnly(23, 59));
        var interval = SnoozePolicy.IntervalFor(LengthOf(window)); // 25% of 9 min → floored to 5

        Assert.Equal(TimeSpan.FromMinutes(5), interval);

        // A re-fire at 11:55p would land inside the day, but the tap comes at the fire...
        Assert.True(SnoozePolicy.IsOffered(At(23, 50), interval, Boundary.EndOf(Date)));

        // ...and by 11:56p there is nowhere for it to land. No "near midnight" constant is invented:
        // the interval is a known number at the instant of the tap.
        Assert.False(SnoozePolicy.IsOffered(At(23, 56), interval, Boundary.EndOf(Date)));

        // A Window whose own length pushes the interval to 30 minutes is refused from its start.
        var longWindow = Window(new TimeOnly(21, 50), new TimeOnly(23, 59));
        Assert.False(SnoozePolicy.IsOffered(
            At(23, 50), SnoozePolicy.IntervalFor(LengthOf(longWindow)), Boundary.EndOf(Date)));
    }

    [Fact]
    public void A_Reminder_that_fired_at_10_30p_and_is_tapped_at_12_05a_offers_no_Snooze()
    {
        // The Reminder fired at 10:30p on Date, and sat on the lock screen until 12:05a the next day.
        var window = Window(new TimeOnly(22, 30), new TimeOnly(23, 30));
        var interval = SnoozePolicy.IntervalFor(LengthOf(window)); // 15 minutes
        var firedAt = At(22, 30);
        var tappedAt = At(0, 5).AddDays(1);

        // The boundary is the *Reminder's* own — the day it fired on, not the day of the tap.
        var reminderBoundary = Boundary.EndOf(Boundary.DateOf(firedAt));

        Assert.True(SnoozePolicy.IsOffered(firedAt, interval, reminderBoundary));
        Assert.False(SnoozePolicy.IsOffered(tappedAt, interval, reminderBoundary));

        // Tonight's boundary would have accepted it — which is exactly the bug the rule refuses.
        var tonightsBoundary = Boundary.EndOf(Boundary.DateOf(tappedAt));
        Assert.True(SnoozePolicy.IsOffered(tappedAt, interval, tonightsBoundary));
    }

    [Fact]
    public void The_ceiling_re_derives_from_the_time_remaining_at_each_re_fire()
    {
        // Garage 2p–3p, from CONTEXT.md's worked example.
        var window = Window(new TimeOnly(14, 0), new TimeOnly(15, 0));
        var end = Resolution.Resolve(Date, window.End);

        // 2:00p fire — 60 minutes left → ceiling 60.
        Assert.Equal(
            new TagValue("60"),
            SnoozePolicy.CeilingFor(SnoozePolicy.RemainingIn(window, end, At(14, 0)), Buckets));

        // 2:45p re-fire — 15 minutes left → ceiling 10, not the 60 it started at.
        Assert.Equal(
            new TagValue("10"),
            SnoozePolicy.CeilingFor(SnoozePolicy.RemainingIn(window, end, At(14, 45)), Buckets));

        // 2:20p — 40 minutes left → 30. Every step reads the clock, never a stored previous ceiling.
        Assert.Equal(
            new TagValue("30"),
            SnoozePolicy.CeilingFor(SnoozePolicy.RemainingIn(window, end, At(14, 20)), Buckets));
    }

    [Fact]
    public void Past_the_Windows_end_the_ceiling_floors_at_the_smallest_bucket_not_at_whatever_was_last_derived()
    {
        var window = Window(new TimeOnly(14, 0), new TimeOnly(15, 0));
        var end = Resolution.Resolve(Date, window.End);
        var smallest = new TagValue("2");

        // 3:05p and 3:25p re-fires: past end → the smallest bucket, not the 10 last derived at 2:45p.
        Assert.Equal(smallest, SnoozePolicy.CeilingFor(SnoozePolicy.RemainingIn(window, end, At(15, 5)), Buckets));
        Assert.Equal(smallest, SnoozePolicy.CeilingFor(SnoozePolicy.RemainingIn(window, end, At(15, 25)), Buckets));

        // Exactly at the end: nothing remains, so it has already floored.
        Assert.Equal(smallest, SnoozePolicy.CeilingFor(TimeSpan.Zero, Buckets));

        // Stateless: hours past the end is the same answer as a minute past it, and the answer does
        // not depend on how the user got there. Nothing accumulates.
        Assert.Equal(smallest, SnoozePolicy.CeilingFor(SnoozePolicy.RemainingIn(window, end, At(22, 0)), Buckets));
        Assert.NotEqual(new TagValue("10"), SnoozePolicy.CeilingFor(TimeSpan.FromMinutes(-1), Buckets));
    }

    [Fact]
    public void Every_other_Dimension_value_stays_frozen_at_the_original_Windows()
    {
        var tags = Merge(
            Values(KnownDimensions.Location, "garage"),
            Values(KnownDimensions.MentalEnergy, "high"),
            new TagSet(new Dictionary<DimensionId, IReadOnlyList<TagValue>>(), [new LooseTag("errand")]));
        var window = Window(new TimeOnly(14, 0), new TimeOnly(15, 0), tags);
        var end = Resolution.Resolve(Date, window.End);

        var context = SnoozePolicy.ReFireContext(
            window,
            SnoozePolicy.RemainingIn(window, end, At(14, 45)),
            Buckets,
            NoFetched,
            Array.Empty<DimensionId>());

        // Only Duration moved.
        Assert.Equal(new TagValue("10"), context.DurationCeiling);
        Assert.Equal(tags, context.Window.Tags);
        Assert.Equal("Garage", context.Window.Name); // the reminder keeps its Window's name throughout

        // Read through the matcher, which is what actually consumes the frozen values: a garage /
        // high-energy Task still fits, a home Task still does not, and a 30-minute Task no longer does.
        var garage = Item(Merge(
            Values(KnownDimensions.Location, "garage"),
            Values(KnownDimensions.MentalEnergy, "high"),
            Values(KnownDimensions.Duration, "10")));
        var home = garage with { Tags = Merge(
            Values(KnownDimensions.Location, "home"),
            Values(KnownDimensions.MentalEnergy, "high"),
            Values(KnownDimensions.Duration, "10")) };
        var thirtyMinutes = garage with { Tags = Merge(
            Values(KnownDimensions.Location, "garage"),
            Values(KnownDimensions.MentalEnergy, "high"),
            Values(KnownDimensions.Duration, "30")) };

        Assert.True(Matcher.Fits(garage, context, Registry));
        Assert.False(Matcher.Fits(home, context, Registry));
        Assert.False(Matcher.Fits(thirtyMinutes, context, Registry));
    }

    [Fact]
    public void An_empty_re_fire_pushes_once_and_ends_the_chain()
    {
        // Answered rather than silently dropped — nobody asked for the Window to stay quiet, but
        // they did ask for this — and then the chain ends, so it cannot become repetitive.
        Assert.Equal(ReFireOutcome.PushAndEndChain, SnoozePolicy.OutcomeOf(0));

        // A non-empty re-fire is an ordinary push and the chain continues.
        Assert.Equal(ReFireOutcome.PushAndContinue, SnoozePolicy.OutcomeOf(1));
        Assert.Equal(ReFireOutcome.PushAndContinue, SnoozePolicy.OutcomeOf(7));
    }
}
