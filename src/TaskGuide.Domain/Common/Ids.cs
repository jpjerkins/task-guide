namespace TaskGuide.Domain.Common;

/// <summary>
/// Type-prefixed ULIDs (#23). The leading 48-bit millisecond timestamp makes lexicographic
/// order chronological order; the prefix costs three characters and makes a stray id
/// self-describing in the audit trail. The timestamp is <em>mint</em> time: a Window in a Day
/// template has no date, and ids must stay stable while start times move.
/// </summary>
public interface IPrefixedId
{
    static abstract string Prefix { get; }
    string Value { get; }
}

public readonly record struct TaskId(string Value) : IPrefixedId
{
    public static string Prefix => "t_";
    public override string ToString() => Value;
}

public readonly record struct WindowId(string Value) : IPrefixedId
{
    public static string Prefix => "w_";
    public override string ToString() => Value;
}

public readonly record struct DayTemplateId(string Value) : IPrefixedId
{
    public static string Prefix => "dt_";
    public override string ToString() => Value;
}

public readonly record struct PatternId(string Value) : IPrefixedId
{
    public static string Prefix => "p_";
    public override string ToString() => Value;
}

public readonly record struct EventId(string Value) : IPrefixedId
{
    public static string Prefix => "evt_";
    public override string ToString() => Value;
}

public readonly record struct EventPrototypeId(string Value) : IPrefixedId
{
    public static string Prefix => "ep_";
    public override string ToString() => Value;
}

/// <summary>
/// Derived-obligation rules live in code (#14), so they are <em>named</em>, not minted.
/// </summary>
public readonly record struct RuleId(string Value)
{
    public override string ToString() => Value;
}

public interface IIdMinter
{
    TaskId NextTaskId();
    WindowId NextWindowId();
    DayTemplateId NextDayTemplateId();
    PatternId NextPatternId();
    EventId NextEventId();
    EventPrototypeId NextEventPrototypeId();
}
