using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Time;

namespace TaskGuide.Domain.Tasks;

/// <summary>
/// A derived label, not a stored field (#47). It remains an eligibility gate: only `Active`
/// Tasks are matched.
/// </summary>
/// <remarks>
/// <b>The order is load-bearing.</b> Derived facts can co-occur where typed values could not —
/// a Task with no Duration can equally be 59 days old — so the rule is <b>first match wins,
/// top to bottom</b>. Exclusivity comes back as a rule rather than as a type, which the two
/// footer counts, the third disjoint Orphan count, and Scarcity's "a Task is only ever in one
/// category" all need.
/// </remarks>
public enum Status
{
    /// <summary>A completion entry covering the current instance. The only authored fact.</summary>
    Done = 0,

    /// <summary>No Duration. Disables marking off — there is nothing yet to be done within.</summary>
    Unprocessed = 1,

    /// <summary>Read off CreatedAt and the completion log; nothing ever writes it.</summary>
    Stale = 2,

    /// <summary>The residue — never set, never displayed as a chip.</summary>
    Active = 3,
}

public sealed record StaleThresholds(TimeSpan UndeadlinedAge, int ConsecutiveMissedInstances);

public static class StatusRules
{
    /// <summary>
    /// Reads the label off the Task. Never stored, never cached — correct the instant the clock
    /// passes a threshold, with no sweep needing to have run.
    /// </summary>
    public static Status Of(
        TaskItem task,
        CompletionLog log,
        DimensionRegistry registry,
        StaleThresholds thresholds,
        DateTimeOffset now,
        DayBoundary boundary)
    {
        // Completion is the only authored fact behind the label, and it outranks everything.
        if (IsDone(task, log, now, boundary))
        {
            return Status.Done;
        }

        // A derived Task is a projection of a trigger through a rule — nobody captured it and
        // nobody neglected it, so neither the missing-Duration rule nor either stale clock has
        // anything to say about one.
        if (task.Provenance is null)
        {
            if (task.Tags.SingleOn(DurationDimension(registry).Id) is null)
            {
                return Status.Unprocessed;
            }

            if (IsStale(task, log, thresholds, now, boundary))
            {
                return Status.Stale;
            }
        }

        return Status.Active;
    }

    /// <summary>
    /// eligible = Active AND now &gt;= Defer AND now &gt;= Postpone. Every term is computed on
    /// read; nothing in the expression is stored.
    /// </summary>
    public static bool IsEligible(
        TaskItem task,
        CompletionLog log,
        DimensionRegistry registry,
        StaleThresholds thresholds,
        DateTimeOffset now,
        DayBoundary boundary)
    {
        var surfacesOn = DeferRules.ResolvedFor(task, log, now, boundary);

        return Of(task, log, registry, thresholds, now, boundary) is Status.Active
            && (surfacesOn is not { } defer || now >= StartOf(defer, boundary))
            && (task.Postpone is not { } postpone || now >= StartOf(postpone, boundary));
    }

    /// <summary>
    /// age = now - max(CreatedAt, Defer). Defer pauses the age clock because a Task that cannot
    /// be started yet cannot be evidence of neglect; <b>Postpone is deliberately absent</b>, and
    /// that omission is the whole difference between the two fields.
    /// </summary>
    public static TimeSpan AgeOf(
        TaskItem task,
        CompletionLog log,
        DateTimeOffset now,
        DayBoundary boundary)
    {
        var from = task.CreatedAt;

        if (DeferRules.ResolvedFor(task, log, now, boundary) is { } defer
            && StartOf(defer, boundary) is var surfaces
            && surfaces > from)
        {
            from = surfaces;
        }

        return now - from;
    }

    /// <summary>
    /// Whether the "Not now" gesture can reach this Task at all — the structural half of
    /// Postpone's "where the gesture appears". The remaining half is the surface's own:
    /// a row only ever appears on a match-driven surface when the Task is eligible.
    /// </summary>
    /// <remarks>
    /// A <b>derived</b> Task is read-only by construction, and a Postpone would be its first
    /// piece of persistent state. A <b>recurring</b> Task's Defer must use the offset form, so
    /// an absolute Postpone on one is unrepresentable — and nothing is lost, because ignoring
    /// the reminder already is skipping.
    /// </remarks>
    public static bool CanPostpone(TaskItem task) => task.Provenance is null && task.Recurrence is null;

    /// <summary>
    /// The instance a completion has to cover: the generated one for a recurring Task, and for
    /// a one-off its Deadline — or null, which is a one-off's `due` when it has none.
    /// </summary>
    private static bool IsDone(TaskItem task, CompletionLog log, DateTimeOffset now, DayBoundary boundary) =>
        task.Recurrence is { } recurrence
            ? log.Covers(RecurrenceRules.LiveInstanceDeadline(recurrence, task.CreatedAt, log, now, boundary))
            : log.Covers(task.Deadline);

    /// <summary>
    /// The trigger differs by task kind, because the same evidence looks different in each —
    /// but wherever <c>CreatedAt</c> is old <em>by design</em>, age measures the wrong thing.
    /// </summary>
    private static bool IsStale(
        TaskItem task,
        CompletionLog log,
        StaleThresholds thresholds,
        DateTimeOffset now,
        DayBoundary boundary) =>
        task.Recurrence is { } recurrence
            // A recurring Task's CreatedAt is ancient by design; consecutive misses carry
            // exactly the meaning age was carrying.
            ? RecurrenceRules.ConsecutiveMissedInstances(recurrence, task.CreatedAt, log, now, boundary)
                >= thresholds.ConsecutiveMissedInstances
            // A Deadline is direct evidence against the proxy age stands in for, so it exempts
            // a one-off from the age rule entirely — before *and* after the Deadline passes.
            : task.Deadline is null && AgeOf(task, log, now, boundary) >= thresholds.UndeadlinedAge;

    /// <summary>
    /// Duration is an ordinal Dimension rather than a field, so "no Duration" is a question for
    /// the registry: the axis this Task carries no value on.
    /// </summary>
    private static Dimension DurationDimension(DimensionRegistry registry) =>
        registry.Dimensions.FirstOrDefault(dimension => dimension.Id == KnownDimensions.Duration)
        ?? throw new InvalidOperationException(
            "The registry declares no Duration Dimension, so `Unprocessed` cannot be derived.");

    /// <summary>The instant a date begins locally — what both gates and the age clock compare against.</summary>
    private static DateTimeOffset StartOf(DateOnly date, DayBoundary boundary) => boundary.EndOf(date.AddDays(-1));
}
