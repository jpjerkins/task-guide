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
    DateTimeOffset TimeToLive);

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
        DateTimeOffset dayBoundary) => throw new NotImplementedException();
}
