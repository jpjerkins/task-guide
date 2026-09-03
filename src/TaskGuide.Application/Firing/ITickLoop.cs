namespace TaskGuide.Application.Firing;

/// <summary>
/// One loop, recomputing on a ~30-second tick. No timers, no scheduled jobs, no startup catch-up
/// sweep — every rule is a predicate about a moment, so recomputation is the natural shape.
/// Downtime is therefore indistinguishable from a slow tick, and the missed-fire policy <em>is</em>
/// the normal path: no catch-up code, and no second implementation to fall out of agreement.
/// </summary>
/// <remarks>
/// Lives in <c>Application</c>, not <c>Domain</c>: it is a <b>driving</b> interface — something
/// external calls into the app, the mirror image of the driven Ports under
/// <c>Application/Ports/</c>. Those name what the app depends on; this names what depends on the
/// app, so it does not belong beside them.
/// </remarks>
public interface ITickLoop
{
    /// <summary>
    /// One pass: fire due Windows, drain pending Snoozes, evaluate the fallback push, and run the
    /// retention sweep unguarded. The sweep's outcome doubles as the store's write health.
    /// </summary>
    Task TickAsync(DateTimeOffset now, CancellationToken cancellationToken);
}
