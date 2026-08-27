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
    IReadOnlyList<EventPrototype> EventPrototypes);

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
        DateOnly today) => throw new NotImplementedException();
}
