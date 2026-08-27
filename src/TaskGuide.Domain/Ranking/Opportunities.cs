using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tasks;

namespace TaskGuide.Domain.Ranking;

/// <summary>
/// How many Availability Windows <em>ahead of now</em> would admit this Task. A plain count, and
/// the input Scarcity ranks on — the two words stay separate because more Opportunities is
/// better for a Task and worse for its rank.
/// </summary>
public sealed class OpportunityCounter(IDayShapeReader shapes, DimensionRegistry registry)
{
    /// <summary>
    /// A rolling <c>min(7 days, time to Deadline)</c> measured from now — not the Pattern's
    /// Sun–Sat week. Once the Deadline has passed the bound is <b>dropped</b> and the horizon
    /// reverts to a plain rolling 7 days; without that it goes negative and every overdue Task
    /// misreports as an Orphan.
    /// </summary>
    public int CountAhead(TaskItem task, DateTimeOffset now) => throw new NotImplementedException();

    /// <summary>
    /// The second count, and the one that tells the two kinds of zero apart: could any Window in
    /// the <em>active Pattern</em> ever admit this Task? Defined for every Task whether or not it
    /// is currently eligible, which is why an absent value is not a zero.
    /// </summary>
    public int CountInPatternWeek(TaskItem task) => throw new NotImplementedException();
}

/// <summary>
/// Two zeroes that look identical and mean opposite things.
/// </summary>
public enum ZeroKind
{
    /// <summary>
    /// No Window in the active Pattern can <em>ever</em> admit it — something is malformed.
    /// Categorically worse than `Unprocessed` or `Stale`, so it gets a badge and a third,
    /// disjoint footer count. The repair is always "declare this Tag on some Window", so the
    /// badge deep-links into the window editor, pre-filtered to the <em>active</em> Pattern's
    /// distinct Day templates that don't yet declare a value on that Tag's Dimension.
    /// </summary>
    Orphan,

    /// <summary>
    /// Normally doable, but every admitting Window falls outside the horizon — usually because
    /// an Override or Event displaced it. Nothing is wrong.
    /// </summary>
    NoneInThisStretch,
}

public static class OrphanDetection
{
    /// <summary>
    /// <b>Respects the Status gate and ignores the clock gates.</b> Only an `Active` Task can be
    /// an Orphan; Defer and Postpone are deliberately not consulted, because orphan-ness asks
    /// whether any Window could <em>ever</em> admit the Task. Tasks only — Events are never
    /// matched — and derived Tasks are included, since an orphaned one means a badly written rule.
    /// </summary>
    public static bool IsOrphan(TaskItem task, Status status, int patternWeekCount) =>
        status == Status.Active && patternWeekCount == 0;
}
