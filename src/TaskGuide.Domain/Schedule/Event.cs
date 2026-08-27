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
