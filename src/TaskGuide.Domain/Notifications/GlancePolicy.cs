namespace TaskGuide.Domain.Notifications;

/// <summary>
/// Recomputed every tick, sent only when the payload differs from the last one <em>sent</em>
/// and the floor has elapsed. A Window <b>start</b> preempts the floor; a Window <b>end</b>
/// does not — the system's model of a span ending is not the same as the user's opportunity
/// ending. One retry at the next tick, ignoring the floor; the failed send spent none of the
/// watch's budget, and the bound of one exists for the send that lost its response.
/// </summary>
public static class GlancePolicy
{
    /// <summary>
    /// Unimplemented — #79 (F1) owns the rule and its tests. <paramref name="floor"/> is supplied
    /// by the caller rather than fixed here: the rule is domain (#69), but the floor's value is a
    /// platform fact (watchOS's 50-updates-a-day budget) that belongs to the adapter that knows it.
    /// </summary>
    public static bool ShouldSend(GlanceState next, GlanceState? lastSent, TimeSpan sinceLastSend, bool windowJustStarted, TimeSpan floor) =>
        lastSent is null
        || (!next.Equals(lastSent) && (windowJustStarted || sinceLastSend >= floor));
}
