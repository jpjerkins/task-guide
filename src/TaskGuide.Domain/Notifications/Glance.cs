namespace TaskGuide.Domain.Notifications;

/// <summary>
/// A silent readout of current state on a watch complication — never a claim, never a doorbell.
/// <b>A Glance is not a Notification</b>: no URL, no sound, no delivery promise, nothing reaching
/// Notification Center. The silence guarantee is therefore untouched, Liveness does not read it,
/// and <b>nothing in this system may depend on it</b>.
/// </summary>
/// <param name="Count">
/// The to-process backlog — the only field that renders unlabelled in a small slot, so it must
/// mean something with no words attached, and it is deliberately window-independent.
/// </param>
/// <remarks>
/// Three text lines, ~20 characters each in practice. Inside a Window with ≥1 match: rank 1,
/// rank 2, "+N more doable now" (required, not decorative — the matching-now total appears
/// nowhere else on the face). Otherwise: the next Window's start, its rank 1, its rank 2 — the
/// fall-through that keeps the Glance from ever being blank, since a dead-looking complication
/// cannot be told apart from a broken one. No Durations, no fetched-failure note.
/// </remarks>
public sealed record Glance(int Count, string Line1, string Line2, string Line3);

public static class GlancePolicy
{
    /// <summary>
    /// Derived, not chosen: watchOS grants 50 widget updates a day, and 24h ÷ 50 = 28.8 minutes.
    /// </summary>
    public static readonly TimeSpan Floor = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Recomputed every tick, sent only when the payload differs from the last one <em>sent</em>
    /// and the floor has elapsed. A Window <b>start</b> preempts the floor; a Window <b>end</b>
    /// does not — the system's model of a span ending is not the same as the user's opportunity
    /// ending. One retry at the next tick, ignoring the floor; the failed send spent none of the
    /// watch's budget, and the bound of one exists for the send that lost its response.
    /// </summary>
    public static bool ShouldSend(Glance next, Glance? lastSent, TimeSpan sinceLastSend, bool windowJustStarted) =>
        throw new NotImplementedException();
}
