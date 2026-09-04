using TaskGuide.Domain.Common;

namespace TaskGuide.Domain.Schedule;

/// <summary>
/// What a date actually looks like: <c>Override[date] ?? Pattern[weekday]</c>, with the date's
/// Events and its recurring instances layered on. Computed on demand — the Pattern is never
/// reified, and reading a day's shape must never write one.
/// </summary>
public sealed record DayShape(
    DateOnly Date,
    IReadOnlyList<AvailabilityWindow> Windows,
    IReadOnlyList<Event> Events,
    bool IsOverridden)
{
    /// <summary><see cref="Windows"/> and <see cref="Events"/> compare as multisets — DayShape
    /// is computed on demand, and nothing reads a position within either.</summary>
    public bool Equals(DayShape? other) =>
        other is not null
        && Date == other.Date
        && IsOverridden == other.IsOverridden
        && StructuralEquality.MultisetEqual(Windows, other.Windows)
        && StructuralEquality.MultisetEqual(Events, other.Events);

    public override int GetHashCode() =>
        HashCode.Combine(
            Date,
            IsOverridden,
            StructuralEquality.MultisetHash(Windows),
            StructuralEquality.MultisetHash(Events));
}

public interface IDayShapeReader
{
    DayShape For(DateOnly date);
}
