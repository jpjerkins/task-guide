using TaskGuide.Domain.Common;

namespace TaskGuide.Domain.Schedule;

/// <summary>
/// A single date whose day shape differs from what the active Pattern assumes. `CONTEXT.md`
/// calls this an <b>Override</b>; the type is <c>DateOverride</c> because <c>override</c> is a
/// C# keyword.
/// </summary>
/// <remarks>
/// <b>The date is the unit.</b> There is no multi-day Override object — a weekend away is two
/// dated Overrides written by one authoring gesture (see <see cref="OverrideSpanRequest"/>).
/// A date has exactly one shape, so conflicts are unrepresentable rather than resolved.
/// <para>
/// <b>Always copied Windows, never a reference.</b> Applying a named Day template is a stamp,
/// not a link. <b>The copy preserves each Window's id</b> — load-bearing for the Fire record,
/// which is keyed on (date, windowId): a date materialised mid-day with fresh ids would read as
/// unfired and push again a minute later.
/// </para>
/// </remarks>
public sealed record DateOverride(
    DateOnly Date,
    IReadOnlyList<AvailabilityWindow> Windows,
    DayTemplateUse? Used)
{
    /// <summary>A one-off day has no use record — nothing was stamped.</summary>
    public bool IsOneOffDay => Used is null;

    /// <summary><see cref="Windows"/> compares as a multiset — a Window is a per-day instance,
    /// not a position (`CONTEXT.md` § Availability Window).</summary>
    public bool Equals(DateOverride? other) =>
        other is not null
        && Date == other.Date
        && StructuralEquality.MultisetEqual(Windows, other.Windows)
        && Equals(Used, other.Used);

    public override int GetHashCode() =>
        HashCode.Combine(Date, StructuralEquality.MultisetHash(Windows), Used);
}

/// <summary>
/// The Day template this date wore, with <b>the name captured at write time</b> — a
/// <em>use record, not a reference and not a provenance</em>. It is never followed to resolve a
/// shape, only counted and displayed; its one job is answering "does this shape still matter?",
/// which is what makes a Day template `Unused` or not.
/// </summary>
/// <remarks>
/// Single-valued: re-stamping a date replaces it. It survives the date becoming a one-off day —
/// nudging one Window on a stamped Christmas does not un-happen the fact that you reached for
/// that shape. Because the name is stored, history stays readable after the template is deleted.
/// <para>
/// This field supersedes #23's "there is deliberately no stampedFrom provenance field", which
/// predates #24. The distinction #23 was protecting survives in the wording: this is not a
/// pointer to be resolved, and a stamped Override and a one-off day are still the same record.
/// </para>
/// </remarks>
public sealed record DayTemplateUse(DayTemplateId TemplateId, string TemplateName);

/// <summary>
/// One authoring gesture over a span of dates, writing <b>one Override per date</b> — each
/// independently editable afterwards. Per-day variation therefore falls out free; nothing has
/// to express "different on day 3".
/// </summary>
/// <remarks>
/// Settled in <b>Spec assembly</b> (#41): the from-scratch gesture takes a start–end range, so
/// `CONTEXT.md`'s "two dated Overrides created in one authoring gesture" is honoured literally.
/// Dates in the range that already carry an Override are <b>replacements</b>, confirmed in one
/// batch before the write — the standing rule that blast radius is made visible, not prevented.
/// </remarks>
public sealed record OverrideSpanRequest(DateOnly From, DateOnly To, DayTemplateId? Stamp)
{
    public IEnumerable<DateOnly> Dates()
    {
        for (var date = From; date <= To; date = date.AddDays(1))
        {
            yield return date;
        }
    }
}
