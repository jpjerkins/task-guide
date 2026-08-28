using TaskGuide.Domain.Tags;

namespace TaskGuide.Domain.Dimensions;

/// <summary>
/// An axis that both a Task and an Availability Window carry a value on — the only thing
/// matching looks at. Declared in code; there is no UI for managing Dimensions.
/// Identity and label are separate, so a rename touches no stored data.
/// </summary>
public abstract record Dimension(DimensionId Id, string Label)
{
    public abstract IReadOnlyList<TagValue> Values { get; }

    /// <summary>
    /// The editor control this Dimension derives, from its algebra alone — never authored.
    /// Categorical axes offer a multi-select; ordinal axes offer a slider, with a
    /// "leave at the default" control only when the axis actually declares a default.
    /// </summary>
    public abstract ControlShape ControlShape { get; }
}

/// <summary>
/// The shape of a Dimension's editor control, derived from its algebra (never configured —
/// see Constraint 7). A categorical axis is always a multi-select; an ordinal axis is always
/// a slider, one that additionally exposes a "leave at the default" control when — and only
/// when — the axis declares a default on that side. Duration declares none, so it has none.
/// </summary>
public abstract record ControlShape
{
    public sealed record MultiSelect : ControlShape;

    public sealed record Slider(bool HasLeaveAtDefault) : ControlShape;
}

/// <summary>Location, With whom, Weather. Both sides carry a set; matching is subset.</summary>
public sealed record CategoricalDimension(
    DimensionId Id,
    string Label,
    IReadOnlyList<TagValue> DeclaredValues,
    WindowValueSource WindowSource = WindowValueSource.Authored)
    : Dimension(Id, Label)
{
    public override IReadOnlyList<TagValue> Values => DeclaredValues;

    // Categorical axes declare NO defaults on either side. Absence is the empty set,
    // and the empty set already does everything a default was doing.

    public override ControlShape ControlShape => new ControlShape.MultiSelect();
}

/// <summary>Mental energy, Duration. One value per side; the Window's is a ceiling.</summary>
public sealed record OrdinalDimension(
    DimensionId Id,
    string Label,
    IReadOnlyList<TagValue> OrderedValues,
    TagValue? TaskDefault,
    TagValue? WindowDefault,
    WindowValueSource WindowSource = WindowValueSource.Authored)
    : Dimension(Id, Label)
{
    public override IReadOnlyList<TagValue> Values => OrderedValues;

    public int RankOf(TagValue value)
    {
        for (var i = 0; i < OrderedValues.Count; i++)
        {
            if (OrderedValues[i] == value) return i;
        }

        return -1;
    }

    /// <summary>
    /// A "leave at the default" control appears only when this axis actually declares a
    /// default — a slider has no position for "unset", so the picker needs an explicit
    /// control to distinguish it from "deliberately set to the default". Duration declares
    /// no default (its absence <em>is</em> `Unprocessed`), so it derives no such control.
    /// </summary>
    public override ControlShape ControlShape => new ControlShape.Slider(HasLeaveAtDefault: TaskDefault is not null);
}

/// <summary>
/// Where a Dimension's <em>window-side</em> value comes from. Duration is Derived (from the
/// Window's length); Weather is Fetched (live, never stored, and only when some Active Task
/// actually carries a Weather Tag).
/// </summary>
public enum WindowValueSource
{
    Authored,
    Derived,
    Fetched,
}

public readonly record struct DimensionId(string Value)
{
    public override string ToString() => Value;
}
