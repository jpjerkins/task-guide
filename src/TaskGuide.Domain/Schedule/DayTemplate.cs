using TaskGuide.Domain.Common;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;

namespace TaskGuide.Domain.Schedule;

/// <summary>
/// A named, reusable day shape — the unit of substitution. Used two different ways, and the
/// difference is load-bearing: <b>by reference</b> in a Pattern (edits propagate) and
/// <b>by value</b> in an Override (the date holds a copy; edits never reach it).
/// </summary>
public sealed record DayTemplate(
    DayTemplateId Id,
    string Name,
    IReadOnlyList<AvailabilityWindow> Windows,
    IReadOnlyList<EventPrototype> EventPrototypes)
{
    /// <summary>
    /// <see cref="Windows"/> and <see cref="EventPrototypes"/> compare as multisets — a Window is
    /// a per-day instance, not a shared definition (`CONTEXT.md` § Availability Window), and an
    /// EventPrototype carries the same relationship to a template — so neither collection's order
    /// carries meaning.
    /// </summary>
    public bool Equals(DayTemplate? other) =>
        other is not null
        && Id.Equals(other.Id)
        && Name == other.Name
        && StructuralEquality.MultisetEqual(Windows, other.Windows)
        && StructuralEquality.MultisetEqual(EventPrototypes, other.EventPrototypes);

    /// <summary>
    /// <see cref="Windows"/> holds <see cref="AvailabilityWindow"/>s and <see cref="EventPrototypes"/>
    /// holds <see cref="EventPrototype"/>s — different element types, so nothing can migrate
    /// between them and the additive-decomposition trap #115 guards against for a Dimension's id
    /// and its values (a swappable pair of the <em>same</em> type) does not apply here. Combined
    /// via <see cref="HashCode.Combine{T1, T2, T3, T4}"/> anyway, simply because there is no
    /// reason to sum instead.
    /// </summary>
    public override int GetHashCode() =>
        HashCode.Combine(
            Id,
            Name,
            StructuralEquality.MultisetHash(Windows),
            StructuralEquality.MultisetHash(EventPrototypes));
}

/// <summary>
/// A dateless Event held by a template, instantiating into a real dated Event when the template
/// is applied — the same relationship a Window already has. Prototypes let a template record
/// <em>why</em> it has its shape instead of carrying an unexplained hole.
/// </summary>
public sealed record EventPrototype(
    EventPrototypeId Id,
    string Name,
    TimeOnly Start,
    TimeOnly End,
    TagSet Tags,
    Offset? AbsenceNotice);

/// <summary>
/// `Unused` ⟺ deletable. One predicate, one name, one consequence — derived and recomputed on
/// read, with no archive action and no stored flag.
/// </summary>
public static class DayTemplateLifecycle
{
    /// <summary>±13 months: one year plus a month of slack, symmetric, and a threshold of one use.</summary>
    public static readonly int UseRecordHorizonMonths = 13;

    /// <summary>
    /// No Pattern references it — active <em>or</em> dormant — and no Override within ±13 months
    /// was stamped from it. A shape referenced only by a dormant Pattern simply is not `Unused`,
    /// so the dangerous delete is unrepresentable rather than warned about.
    /// </summary>
    public static bool IsUnused(
        DayTemplateId template,
        IReadOnlyList<Pattern> allPatterns,
        IReadOnlyList<DateOverride> overrides,
        DateOnly today)
    {
        var referencedByAnyPattern = allPatterns.Any(p => p.Days.Contains(template));
        if (referencedByAnyPattern)
        {
            return false;
        }

        var earliest = today.AddMonths(-UseRecordHorizonMonths);
        var latest = today.AddMonths(UseRecordHorizonMonths);
        var stampedWithinHorizon = overrides.Any(o =>
            o.Used?.TemplateId == template && o.Date >= earliest && o.Date <= latest);

        return !stampedWithinHorizon;
    }
}
