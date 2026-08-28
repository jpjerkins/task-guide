using TaskGuide.Domain.Time;

namespace TaskGuide.Domain.Tasks;

/// <summary>
/// The earliest time a Task may surface — because it cannot sensibly be started before then.
/// A fact about the Task ("shouldn't start before"), never a way of pushing it away; the
/// reactive gesture is Postpone, a separate field.
/// </summary>
public abstract record Defer
{
    public abstract DateOnly Resolve(DateOnly? deadline);
}

public sealed record AbsoluteDefer(DateOnly Date) : Defer
{
    public override DateOnly Resolve(DateOnly? deadline) => Date;
}

/// <summary>Recurring Tasks must use this form — an absolute date would be wrong forever after.</summary>
public sealed record OffsetDefer(Offset Offset) : Defer
{
    public override DateOnly Resolve(DateOnly? deadline) => deadline is { } anchor
        ? Offset.ResolveAgainst(anchor)
        : throw new InvalidOperationException(
            "An offset Defer is a function of the Deadline, so a Task with none has nothing to "
            + "anchor it against.");
}

/// <summary>
/// Resolving a Task's Defer to the date it actually surfaces on.
/// </summary>
public static class DeferRules
{
    /// <summary>
    /// The date this Task surfaces on, or null if it carries no Defer. Computed on read and
    /// never stored: a recurring Task's anchor is the instance live at <paramref name="now"/>,
    /// so the answer moves with the generator, per instance.
    /// </summary>
    public static DateOnly? ResolvedFor(
        TaskItem task,
        CompletionLog log,
        DateTimeOffset now,
        DayBoundary boundary)
    {
        if (task.Defer is not { } defer)
        {
            return null;
        }

        if (task.Recurrence is not { } recurrence)
        {
            return defer.Resolve(task.Deadline);
        }

        if (defer is AbsoluteDefer)
        {
            throw new InvalidOperationException(
                "A recurring Task must express its Defer as an Offset: an absolute date would "
                + "apply to one instance and be wrong forever after.");
        }

        return defer.Resolve(RecurrenceRules.LiveInstanceDeadline(recurrence, task.CreatedAt, log, now, boundary));
    }
}
