using TaskGuide.Domain.Common;

namespace TaskGuide.Domain.Firing;

/// <summary>
/// What has fired and what is still owed today, keyed <c>(date, windowId, kind)</c>.
/// One file per day, <c>/data/fires/&lt;date&gt;.json</c>, 30-day retention by whole files.
/// </summary>
/// <remarks>
/// <b>Fires and pending Snoozes are the same structure</b>: a fire is a row with a
/// <c>FiredAt</c>, a pending Snooze is a row with a future <c>DueAt</c> and a null
/// <c>FiredAt</c>. One file and one loop serve both, which is what lets a Snooze survive a
/// restart with no machinery of its own.
/// <para>
/// <b>Times are instants.</b> A bare "17:45" would be ambiguous by an hour on the fall-back day,
/// in the one record whose job is answering "why did I not get a reminder?".
/// </para>
/// <para>
/// The row carries the Window's <b>name and span as they were when it fired</b>. The Window may
/// since have been edited, retagged or deleted — this is the one thing an id could never carry
/// honestly, however much were encoded into it.
/// </para>
/// </remarks>
public sealed record FireRow(
    WindowId? WindowId,
    FireKind Kind,
    string? WindowName,
    TimeOnly? WindowStart,
    TimeOnly? WindowEnd,
    DateTimeOffset? DueAt,
    DateTimeOffset? FiredAt,
    int? Matched,
    EventId? Carried)
{
    /// <summary>The engine's whole idempotency guarantee: no row with a FiredAt for that key.</summary>
    public bool IsFired => FiredAt is not null;

    /// <summary>
    /// Every pending Snooze row is one that will fire — a DueAt past the Day boundary cannot be
    /// written, so the file never holds a dead row for the loop to carry to midnight.
    /// </summary>
    public bool IsPendingSnooze => Kind == FireKind.Snooze && FiredAt is null;
}

public enum FireKind
{
    Window,

    /// <summary>The day's first Window firing whether or not Tasks match, carrying an Event's footer.</summary>
    Unconditional,

    Snooze,

    /// <summary>The one row with no Window behind it, so its WindowId is null.</summary>
    Fallback,
}

public sealed record DayFires(DateOnly Date, IReadOnlyList<FireRow> Rows);
