using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Matching;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;

namespace TaskGuide.Domain.Firing;

/// <summary>
/// The only re-fire path, always user-initiated from the landing page a notification opens.
/// </summary>
public static class SnoozePolicy
{
    public static readonly TimeSpan Floor = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan Cap = TimeSpan.FromMinutes(30);

    /// <summary>clamp(25% of Window duration, 5 minutes, 30 minutes). Repeats are unlimited, same interval each time.</summary>
    public static TimeSpan IntervalFor(TimeSpan windowLength)
    {
        var quarter = windowLength * 0.25;

        return quarter < Floor ? Floor
            : quarter > Cap ? Cap
            : quarter;
    }

    /// <summary>
    /// <c>offered ⟺ now + interval &lt; the Reminder's Day boundary</c>. Nothing here is a
    /// threshold: the interval is a known number at the instant of the tap, so "will this Snooze
    /// ever fire?" is decidable rather than estimated. <b>The boundary is the Reminder's own</b>,
    /// which covers both a Window firing at 11:50p and a page tapped at 12:05a.
    /// </summary>
    /// <remarks>
    /// The predicate is server-side and the UI reads it; the endpoint rejects a crossing request
    /// and the rejection renders as the same line the disabled state would have shown — a
    /// rejection is just a slow tick, read onto a button. There is no client-side clock.
    /// </remarks>
    public static bool IsSnoozeOffered(DateTimeOffset now, TimeSpan interval, DateTimeOffset reminderDayBoundary) =>
        now + interval < reminderDayBoundary;

    /// <summary>
    /// Every Dimension value is frozen at the original Window's, <b>except Duration</b>, whose
    /// ceiling is re-derived from the time actually remaining — and floors at the smallest bucket
    /// once the span is spent, which keeps the rule stateless and independent of when the user
    /// happened to snooze. An empty re-fire pushes once, then ends the chain.
    /// </summary>
    public static TimeSpan RemainingIn(DateTimeOffset end, DateTimeOffset now) =>
        end - now;

    /// <summary>
    /// The re-derived Duration ceiling: from the time <b>actually remaining</b> while any is
    /// left, and floored at the smallest bucket once the span is spent. Flooring at the
    /// smallest bucket rather than at whatever was last derived is what keeps the rule
    /// <b>stateless</b> — no chain remembers anything, and the answer does not depend on when
    /// the user happened to snooze or how many times.
    /// </summary>
    public static TagValue CeilingFor(TimeSpan remaining, IReadOnlyList<TagValue> orderedBuckets) =>
        remaining > TimeSpan.Zero
            ? DurationCeiling.WindowCeiling(remaining, orderedBuckets)
            : SmallestBucketOf(orderedBuckets);

    /// <summary>
    /// The smallest bucket that names a minute count, read off the registry's own declared
    /// values — never a literal restating them. It is identified by being the first sized one,
    /// the same way <see cref="DurationCeiling"/> identifies <c>longer</c> by what it is not.
    /// </summary>
    private static TagValue SmallestBucketOf(IReadOnlyList<TagValue> orderedBuckets) =>
        orderedBuckets.First(bucket => int.TryParse(bucket.Value, out _));

    /// <summary>
    /// The re-fire's filter: the original Window as it stands, so every Dimension value stays
    /// frozen at its authored value — only Duration moves, to <see cref="CeilingFor"/>.
    /// </summary>
    public static MatchContext ReFireContext(
        AvailabilityWindow window,
        TimeSpan remaining,
        IReadOnlyList<TagValue> orderedBuckets,
        IReadOnlyDictionary<DimensionId, IReadOnlyList<TagValue>> fetched,
        IReadOnlyList<DimensionId> failedFetches) =>
        new(window, CeilingFor(remaining, orderedBuckets), fetched, failedFetches);

    /// <summary>
    /// <b>An empty re-fire pushes once, then ends the chain.</b> The request is answered rather
    /// than silently dropped — nobody asked for the Window to stay quiet, but they did ask for
    /// this — and it cannot become repetitive, because once the ceiling has floored every later
    /// re-fire runs a strictly narrower query and can only repeat the same emptiness.
    /// </summary>
    public static ReFireOutcome OutcomeOf(int matchedCount) =>
        matchedCount == 0 ? ReFireOutcome.PushAndEndChain : ReFireOutcome.PushAndContinue;
}

/// <summary>What a re-fire does. Both cases push: the difference is whether Snooze is offered again.</summary>
public enum ReFireOutcome
{
    PushAndContinue,
    PushAndEndChain,
}
