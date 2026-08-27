using TaskGuide.Domain.Dimensions;

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
        DateTimeOffset now) => throw new NotImplementedException();

    /// <summary>
    /// eligible = Active AND now &gt;= Defer AND now &gt;= Postpone. Every term is computed on
    /// read; nothing in the expression is stored.
    /// </summary>
    public static bool IsEligible(
        TaskItem task,
        CompletionLog log,
        DimensionRegistry registry,
        StaleThresholds thresholds,
        DateTimeOffset now) => throw new NotImplementedException();

    /// <summary>
    /// age = now - max(CreatedAt, Defer). Defer pauses the age clock because a Task that cannot
    /// be started yet cannot be evidence of neglect; <b>Postpone is deliberately absent</b>, and
    /// that omission is the whole difference between the two fields.
    /// </summary>
    public static TimeSpan AgeOf(TaskItem task, DateTimeOffset now) => throw new NotImplementedException();
}
