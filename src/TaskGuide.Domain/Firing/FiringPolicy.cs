using TaskGuide.Domain.Schedule;

namespace TaskGuide.Domain.Firing;

/// <summary>
/// Every Availability Window fires at its start time. There is no per-Window notify flag, no
/// day-level budget, no cap and no rate limit — restraint is enforced in exactly one place, the
/// matcher: <b>if no Tasks match, no Reminder fires</b>.
/// </summary>
/// <remarks>
/// That makes silence information: <c>no Reminder ⟺ nothing fit</c>, given Liveness. Any cap
/// weakens it to "nothing fit OR budget spent", at which point a quiet afternoon is unreadable.
/// <para>
/// <b>Opportunities die with their span. Obligations die at midnight.</b> A missed Window fires
/// late any time it is still inside its own span, with the Duration ceiling re-derived from
/// <c>now → end</c>; once the span closes it is silent. The Window's own span <em>is</em> the
/// grace period — there is no constant to tune, and a long outage produces no burst on return.
/// </para>
/// </remarks>
public static class FiringPolicy
{
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    /// <summary>A constant in the rules layer, not a setting.</summary>
    public static readonly TimeOnly FallbackPushEarliest = new(11, 00);

    public static readonly int EventRunwayDays = 3;

    public static bool IsDue(AvailabilityWindow window, DateTimeOffset start, DateTimeOffset now) =>
        start <= now;

    public static bool IsAlive(AvailabilityWindow window, DateTimeOffset end, DateTimeOffset now) =>
        now < end;
}

/// <summary>
/// One loop, recomputing on a ~30-second tick. No timers, no scheduled jobs, no startup catch-up
/// sweep — every rule is a predicate about a moment, so recomputation is the natural shape.
/// Downtime is therefore indistinguishable from a slow tick, and the missed-fire policy <em>is</em>
/// the normal path: no catch-up code, and no second implementation to fall out of agreement.
/// </summary>
public interface ITickLoop
{
    /// <summary>
    /// One pass: fire due Windows, drain pending Snoozes, evaluate the fallback push, and run the
    /// retention sweep unguarded. The sweep's outcome doubles as the store's write health.
    /// </summary>
    Task TickAsync(DateTimeOffset now, CancellationToken cancellationToken);
}
