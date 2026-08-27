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
    /// <summary>
    /// Ambiguous (fall back): the <b>first</b> occurrence — a Window fires at its start.
    /// Nonexistent (spring forward): <b>clamp</b> to the first valid instant, i.e. the gap's end.
    /// </summary>
    public DateTimeOffset Resolve(DateOnly date, TimeOnly clockTime) => throw new NotImplementedException();

    /// <summary>
    /// Measured between instants, so a span crossing a transition is honestly an hour shorter or
    /// longer. A Window lying entirely inside the spring gap clamps to zero length — and a
    /// zero-length Window is no opportunity at all, so it does not fire. No rule of its own.
    /// </summary>
    public TimeSpan LengthOf(DateOnly date, TimeOnly start, TimeOnly end) => throw new NotImplementedException();
}
