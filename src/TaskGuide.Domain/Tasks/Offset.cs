namespace TaskGuide.Domain.Tasks;

/// <summary>
/// A date expressed relative to another date. A closed set of exactly two forms, serving
/// Defer's offset form, an Event's Absence notice, and derived-obligation deadlines alike.
/// Deliberately <em>not</em> unified with Recurrence: a Recurrence rule is a generator, an
/// Offset is a function of one date.
/// </summary>
public abstract record Offset
{
    public abstract DateOnly ResolveAgainst(DateOnly anchor);
}

public sealed record BeforeOffset(int N, OffsetUnit Unit) : Offset
{
    public override DateOnly ResolveAgainst(DateOnly anchor) => Unit switch
    {
        OffsetUnit.Days => anchor.AddDays(-N),
        OffsetUnit.Weeks => anchor.AddDays(-N * 7),
        // AddMonths clamps to the last valid day of the resulting month (e.g. Mar 31 - 1mo =
        // Feb 28/29) — it neither throws nor rolls into an adjacent month.
        OffsetUnit.Months => anchor.AddMonths(-N),
        _ => throw new ArgumentOutOfRangeException(nameof(Unit), Unit, "Unknown offset unit."),
    };
}

/// <summary>
/// "The Friday preceding" — <em>strictly</em> before, so an anchor that falls on a Friday
/// resolves to the week before, not to its own morning.
/// </summary>
public sealed record LastWeekdayBefore(DayOfWeek Weekday) : Offset
{
    public override DateOnly ResolveAgainst(DateOnly anchor)
    {
        var candidate = anchor.AddDays(-1);
        while (candidate.DayOfWeek != Weekday)
        {
            candidate = candidate.AddDays(-1);
        }

        return candidate;
    }
}

public enum OffsetUnit { Days, Weeks, Months }
