namespace TaskGuide.Domain.Time;

/// <summary>
/// <b>Authored values are clock times; recorded facts are instants.</b> This resolves the first
/// into the second, per date, and it is where "real arithmetic, not a vibe" is enforced.
/// </summary>
/// <remarks>
/// Freezing each Window to a UTC instant at authoring time was rejected: an "Evening prep"
/// authored in July would start at 4:30p in November. Treating the wall clock as authoritative
/// for duration was rejected for handing the matcher a 60-minute ceiling on a day that only
/// contains 60.
/// </remarks>
public sealed class ClockTimeResolution(DayBoundary boundary)
{
    private readonly DayBoundary _boundary = boundary;


    /// <summary>
    /// Ambiguous (fall back): the <b>first</b> occurrence — a Window fires at its start.
    /// Nonexistent (spring forward): <b>clamp</b> to the first valid instant, i.e. the gap's end.
    /// </summary>
    public DateTimeOffset Resolve(DateOnly date, TimeOnly clockTime)
    {
        var zone = _boundary.Zone;
        var local = date.ToDateTime(clockTime);

        if (zone.IsInvalidTime(local))
        {
            // Clamp to the gap's end: step forward to the first valid local time, then resolve
            // that. US transitions land on the minute, so this terminates exactly on the boundary.
            do
            {
                local = local.AddMinutes(1);
            } while (zone.IsInvalidTime(local));

            return new DateTimeOffset(local, zone.GetUtcOffset(local));
        }

        if (zone.IsAmbiguousTime(local))
        {
            // The first occurrence is the one still on daylight time — it comes first in real
            // time, before the clocks fall back.
            var firstOffset = zone.GetAmbiguousTimeOffsets(local).Max();
            return new DateTimeOffset(local, firstOffset);
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    /// <summary>
    /// Measured between instants, so a span crossing a transition is honestly an hour shorter or
    /// longer. A Window lying entirely inside the spring gap clamps to zero length — and a
    /// zero-length Window is no opportunity at all, so it does not fire. No rule of its own.
    /// </summary>
    public TimeSpan LengthOf(DateOnly date, TimeOnly start, TimeOnly end) => Resolve(date, end) - Resolve(date, start);
}
