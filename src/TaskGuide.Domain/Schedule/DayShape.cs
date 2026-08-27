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
    bool IsOverridden);

public interface IDayShapeReader
{
    DayShape For(DateOnly date);
}
