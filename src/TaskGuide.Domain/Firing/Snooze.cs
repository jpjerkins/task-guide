using TaskGuide.Domain.Schedule;

namespace TaskGuide.Domain.Firing;

/// <summary>
/// The only re-fire path, always user-initiated from the landing page a notification opens.
/// </summary>
public static class SnoozePolicy
{
    public static readonly TimeSpan Floor = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan Cap = TimeSpan.FromMinutes(30);

    /// <summary>clamp(25% of Window duration, 5 minutes, 30 minutes). Repeats are unlimited, same interval each time.</summary>
    public static TimeSpan IntervalFor(TimeSpan windowLength) => throw new NotImplementedException();

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
    public static bool IsOffered(DateTimeOffset now, TimeSpan interval, DateTimeOffset reminderDayBoundary) =>
        now + interval < reminderDayBoundary;

    /// <summary>
    /// Every Dimension value is frozen at the original Window's, <b>except Duration</b>, whose
    /// ceiling is re-derived from the time actually remaining — and floors at the smallest bucket
    /// once the span is spent, which keeps the rule stateless and independent of when the user
    /// happened to snooze. An empty re-fire pushes once, then ends the chain.
    /// </summary>
    public static TimeSpan RemainingIn(AvailabilityWindow window, DateTimeOffset end, DateTimeOffset now) =>
        end - now;
}
