using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;

namespace TaskGuide.Domain.Matching;

/// <summary>
/// A Dimension's matching rule is fixed by its <b>algebra</b>, not written per Dimension.
/// Rules are per-Dimension only and never read another axis's values; a Task must satisfy
/// <em>every</em> Dimension, whatever each one's algebra.
/// </summary>
public static class Matcher
{
    /// <summary>
    /// Window values OR together, Task values AND together: a Task fits when its set on that
    /// axis is a <b>subset</b> of the Window's. Read as constraints and conditions this is one
    /// rule, not two — the Task's set is what must be true, the Window's is what is true.
    /// </summary>
    public static bool CategoricalFits(
        IReadOnlyList<TagValue> taskValues,
        IReadOnlyList<TagValue> windowValues) => throw new NotImplementedException();

    /// <summary>Task value ≤ Window value → fits. Each side's default applies when it is silent.</summary>
    public static bool OrdinalFits(
        OrdinalDimension dimension,
        TagValue? taskValue,
        TagValue? windowValue) => throw new NotImplementedException();

    /// <summary>A conjunction across axes, with loose Tags ignored: inert means inert to matching.</summary>
    public static bool Fits(
        TaskItem task,
        MatchContext window,
        DimensionRegistry registry) => throw new NotImplementedException();
}

/// <summary>
/// A Window resolved into the values matching actually compares against: authored values as
/// stored, Duration derived from the length still available, and any fetched axis filled in.
/// </summary>
/// <param name="DurationCeiling">
/// Derived from the Window's length — or, on a Snooze re-fire, from the time <em>actually
/// remaining</em>, flooring at the smallest bucket once the span is spent.
/// </param>
/// <param name="Fetched">
/// Fetched axes (Weather), filled at evaluation time and never stored. Lazy: the check only runs
/// if some Active Task actually carries a value on that axis.
/// </param>
/// <param name="FailedFetches">
/// Unknown resolves to the <b>empty set</b> — fails closed, the same rule absence already
/// follows. Named in the reminder footer when the moment is UI-visible; silent when headless,
/// because nobody is there to read it.
/// </param>
public sealed record MatchContext(
    AvailabilityWindow Window,
    TagValue DurationCeiling,
    IReadOnlyDictionary<DimensionId, IReadOnlyList<TagValue>> Fetched,
    IReadOnlyList<DimensionId> FailedFetches);
