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
    public override DateOnly Resolve(DateOnly? deadline) => throw new NotImplementedException();
}
