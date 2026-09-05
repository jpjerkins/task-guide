using OneOf;

namespace TaskGuide.Domain.Tasks;

/// <summary>
/// A date expressed relative to another date. A closed set of exactly two forms, serving
/// Defer's offset form, an Event's Absence notice, and derived-obligation deadlines alike.
/// Deliberately <em>not</em> unified with Recurrence: a Recurrence rule is a generator, an
/// Offset is a function of one date.
/// </summary>
[GenerateOneOf]
public partial class Offset : OneOfBase<BeforeOffset, LastWeekdayBefore>;

public sealed record BeforeOffset(int N, OffsetUnit Unit);

/// <summary>
/// "The Friday preceding" — <em>strictly</em> before, so an anchor that falls on a Friday
/// resolves to the week before, not to its own morning.
/// </summary>
public sealed record LastWeekdayBefore(DayOfWeek Weekday);

/// <summary>
/// Resolving an Offset's date, per its case.
/// </summary>
public static class OffsetRules
{
    public static DateOnly ResolveAgainst(Offset offset, DateOnly anchor) => offset.Match(
        before => before.Unit switch
        {
            OffsetUnit.Days => anchor.AddDays(-before.N),
            OffsetUnit.Weeks => anchor.AddDays(-before.N * 7),
            // AddMonths clamps to the last valid day of the resulting month (e.g. Mar 31 - 1mo =
            // Feb 28/29) — it neither throws nor rolls into an adjacent month.
            OffsetUnit.Months => anchor.AddMonths(-before.N),
            _ => throw new ArgumentOutOfRangeException(nameof(before.Unit), before.Unit, "Unknown offset unit."),
        },
        last =>
        {
            var candidate = anchor.AddDays(-1);
            while (candidate.DayOfWeek != last.Weekday)
            {
                candidate = candidate.AddDays(-1);
            }

            return candidate;
        });
}

public enum OffsetUnit { Days, Weeks, Months }
