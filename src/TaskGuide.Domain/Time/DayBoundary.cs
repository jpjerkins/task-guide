namespace TaskGuide.Domain.Time;

/// <summary>
/// Local midnight, in the service's fixed zone. One definition, used identically everywhere the
/// system needs to know what day it is — Snooze expiry, obligation catch-up, the fallback push
/// bound, the 3-day Event runway, Recurrence due dates, and the `Stale` age rule.
/// <b>Nothing in the model may disagree about what day it is.</b>
/// </summary>
/// <remarks>
/// Midnight survives DST for free: US transitions happen at 2a, so 00:00 is never ambiguous and
/// never missing. A configurable rollover hour was rejected as a tunable that would make "what
/// day is it" answerable two different ways.
/// </remarks>
public sealed class DayBoundary(TimeZoneInfo zone)
{
    public static readonly string ZoneId = "America/Chicago";

    public TimeZoneInfo Zone { get; } = zone;

    public DateOnly DateOf(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Zone).DateTime);

    /// <summary>The instant the given date ends — i.e. the next local midnight.</summary>
    public DateTimeOffset EndOf(DateOnly date)
    {
        var nextMidnightLocal = date.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var offset = Zone.GetUtcOffset(nextMidnightLocal);
        return new DateTimeOffset(nextMidnightLocal, offset);
    }
}
