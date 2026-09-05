using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Tasks;

namespace TaskGuide.Domain.Notifications;

/// <summary>
/// A notification is a doorbell carrying one URL — Pushover has no actionable notifications, so
/// every control lives on the landing page it opens. There are two species and the distinction
/// is load-bearing: a <b>Reminder</b> asserts something about this moment's opportunity; a
/// <b>Receipt</b> confirms a capture.
/// </summary>
/// <param name="Title">The top-ranked Task in full, with its Duration — the one line never truncated.</param>
/// <param name="Shortlist">Three, then "+N more" when N ≥ 1. The landing page shows all matches.</param>
/// <param name="Events">Above the Tasks, one line each, date ascending — obligation outranks opportunity.</param>
/// <param name="Footer">Last, and zero-valued components are omitted; if all are zero the line disappears.</param>
public sealed record Reminder(
    string Title,
    string WindowContext,
    IReadOnlyList<TaskItem> Shortlist,
    int MoreCount,
    IReadOnlyList<EventLine> Events,
    FooterCounts Footer,
    IReadOnlyList<DimensionId> FailedFetches,
    Uri LandingPage,
    DateTimeOffset TimeToLive)
{
    /// <summary>
    /// <see cref="Shortlist"/> and <see cref="Events"/> compare as sequences — the shortlist is
    /// ranked and the events are date-ascending, so a reorder is a different Reminder.
    /// <see cref="FailedFetches"/> compares as a multiset — a set of Dimension ids. This record
    /// has the same shape <c>GlanceState</c> did (#69/ADR-0011), so this fix exists to keep a
    /// future Reminder floor from inheriting the identical bug — <see cref="TimeToLivePolicy"/>
    /// is still unimplemented and untouched here.
    /// </summary>
    public bool Equals(Reminder? other)
    {
        if (other is null) return false;

        return Title == other.Title
            && WindowContext == other.WindowContext
            && MoreCount == other.MoreCount
            && Footer.Equals(other.Footer)
            && LandingPage.Equals(other.LandingPage)
            && TimeToLive.Equals(other.TimeToLive)
            && StructuralEquality.SequenceEqual(Shortlist, other.Shortlist)
            && StructuralEquality.SequenceEqual(Events, other.Events)
            && StructuralEquality.MultisetEqual(FailedFetches, other.FailedFetches);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Title);
        hash.Add(WindowContext);
        hash.Add(MoreCount);
        hash.Add(Footer);
        hash.Add(LandingPage);
        hash.Add(TimeToLive);
        hash.Add(StructuralEquality.SequenceHash(Shortlist));
        hash.Add(StructuralEquality.SequenceHash(Events));
        hash.Add(StructuralEquality.MultisetHash(FailedFetches));
        return hash.ToHashCode();
    }
}

public sealed record EventLine(EventId Id, string Name, DayOfWeek Weekday);

/// <summary>
/// A nudge, not news — which is why they are the right thing to lose off the bottom edge.
/// The three counts are <b>disjoint</b>: a Task is only ever in one category, and an Orphan is
/// never counted in the other two.
/// </summary>
public sealed record FooterCounts(int ToProcess, int Stale, int Orphans);

/// <summary>
/// Derived from the same boundary that governs the fire, so it introduces no new concept —
/// only the governing line applied to the notification's afterlife. Verified (#15): an expired
/// message clears from both Notification Center and the Pushover message list.
/// </summary>
public static class TimeToLivePolicy
{
    public static readonly TimeSpan Receipt = TimeSpan.FromHours(24);

    public static DateTimeOffset For(
        Firing.FireKind kind,
        DateTimeOffset windowEnd,
        DateTimeOffset dayBoundary,
        DateTimeOffset now) => kind switch
        {
            Firing.FireKind.Window => windowEnd,
            Firing.FireKind.Snooze when now < windowEnd => windowEnd,
            Firing.FireKind.Snooze => dayBoundary,
            Firing.FireKind.Unconditional => dayBoundary,
            Firing.FireKind.Fallback => dayBoundary,
            var unexpected => throw new ArgumentOutOfRangeException(nameof(kind), unexpected, null),
        };
}
