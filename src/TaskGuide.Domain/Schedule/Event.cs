using TaskGuide.Domain.Common;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;

namespace TaskGuide.Domain.Schedule;

/// <summary>
/// A dated, clock-timed thing the user must attend. Not a Task and not an Availability Window:
/// a Window fires only when Tasks match it, so an obligation expressed as a Window would be
/// silently swallowed by the restraint mechanism; and every Task here is opportunistic, which a
/// concert is the opposite of.
/// </summary>
/// <remarks>
/// <b>Events are never matched.</b> An Event's Tags exist to trigger derived-obligation rules —
/// and those read loose Tags by design, `#timeoff` being the first.
/// </remarks>
public sealed record Event(
    EventId Id,
    DateOnly Date,
    string Name,
    TimeOnly Start,
    TimeOnly End,
    TagSet Tags,
    Offset? AbsenceNotice);

/// <summary>
/// A single date on which a recurring Event differs from what its prototype assumes — moved,
/// renamed, or not happening at all. Keyed (date, prototypeId).
/// </summary>
/// <remarks>
/// <b>Covers edit as well as delete.</b> A delete-only tombstone was rejected as the same record
/// with a capability withheld: expressing a move as delete-plus-create-a-dated-Event would
/// silently change whether the absence rule fires, since a deleted instance is absence and a
/// moved one is not. Deleting an instance does <b>not</b> stamp an Override.
/// </remarks>
public sealed record EventException(
    DateOnly Date,
    EventPrototypeId PrototypeId,
    bool Deleted,
    string? Name,
    TimeOnly? Start,
    TimeOnly? End);

/// <summary>
/// Creating an Event that overlaps an existing Window — even partially — prompts for how to
/// handle that Window. One user action, one question, two artifacts: the Event, <b>and</b> the
/// one-off day it generates.
/// </summary>
public enum OverlapResolution
{
    Replace,

    /// <summary>Truncating the start moves the fire time; truncating the end does not.</summary>
    TruncateStart,
    TruncateEnd,
    Split,
}

/// <summary>
/// Materialises a Day template's dateless <see cref="EventPrototype"/>s into dated <see
/// cref="Event"/>s for one date (<see cref="On"/>), and applies an <see cref="EventException"/>
/// for that date on top of whatever Events the date already has (<see cref="WithExceptions"/>).
/// Shared by <c>DayShapeReader</c>, <c>CreateOverrideSpan.Freeze</c> and
/// <c>DayTemplateLifecycle.Stamp</c> (#153) — moved here verbatim from <c>DayShapeReader</c>,
/// which owned it privately before Freeze and Stamp needed it too.
/// </summary>
/// <remarks>
/// <b>An exception is a separate stored fact the Override never absorbs</b> (#153 review finding
/// 1/5). <c>Freeze</c> and <c>Stamp</c> call only <see cref="On"/> — the raw materialisation — and
/// never bake an exception into what they capture or lay down; <see cref="WithExceptions"/> is
/// applied at read, always, over whichever half the date actually has (an Override's own Events,
/// or <see cref="On"/>'s output). This mirrors `CONTEXT.md` § Event exception's "deleting an
/// instance does not stamp an Override" in the other direction: stamping or freezing a date does
/// not swallow a deletion either, so the shape a user sees and what <c>AbsenceRule</c> derives from
/// can never disagree.
/// </remarks>
public static class RecurringEvents
{
    /// <summary>
    /// <b>Preserves the id format exactly</b> — <c>evt_rec_{date:yyyyMMdd}_{prototypeId}</c> — so a
    /// recurring instance's id is the same on two reads of the same date; #24 makes Fire rows keyed
    /// on that id load-bearing, and it is also what <see cref="WithExceptions"/> matches on.
    /// </summary>
    public static IReadOnlyList<Event> On(DateOnly date, IReadOnlyList<EventPrototype> prototypes) =>
        [.. prototypes.Select(prototype => new Event(
            RecurringEventId(date, prototype.Id),
            date,
            prototype.Name,
            prototype.Start,
            prototype.End,
            prototype.Tags,
            prototype.AbsenceNotice))];

    /// <summary>
    /// Applies each Event exception dated <paramref name="date"/> to whichever <paramref
    /// name="events"/> the date already has. Matches by <b>id</b> — the recurring id <see
    /// cref="On"/> would have minted for (date, prototypeId) — because an <see cref="Event"/>
    /// carries no PrototypeId of its own; a promoted or hand-authored one-off Event's id is never
    /// that shape, so it is always left untouched.
    /// </summary>
    public static IReadOnlyList<Event> WithExceptions(
        DateOnly date, IReadOnlyList<Event> events, IReadOnlyList<EventException> exceptions) =>
        events.SelectMany(@event => Apply(date, @event, exceptions)).ToArray();

    private static IEnumerable<Event> Apply(DateOnly date, Event @event, IReadOnlyList<EventException> exceptions)
    {
        var exception = exceptions.SingleOrDefault(e => e.Date == date && RecurringEventId(date, e.PrototypeId) == @event.Id);
        if (exception is null)
        {
            yield return @event;
            yield break;
        }

        if (exception.Deleted)
        {
            yield break;
        }

        yield return @event with
        {
            Name = exception.Name ?? @event.Name,
            Start = exception.Start ?? @event.Start,
            End = exception.End ?? @event.End,
        };
    }

    private static EventId RecurringEventId(DateOnly date, EventPrototypeId prototype) =>
        new($"evt_rec_{date:yyyyMMdd}_{prototype.Value}");
}
