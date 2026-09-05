using OneOf;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Tags;

namespace TaskGuide.Domain.Dimensions;

/// <summary>
/// An axis that both a Task and an Availability Window carry a value on — the only thing
/// matching looks at. Declared in code; there is no UI for managing Dimensions.
/// Identity and label are separate, so a rename touches no stored data.
/// </summary>
[GenerateOneOf]
public partial class Dimension : OneOfBase<CategoricalDimension, OrdinalDimension>
{
    public DimensionId Id => Match(c => c.Id, o => o.Id);

    public string Label => Match(c => c.Label, o => o.Label);

    public IReadOnlyList<TagValue> Values => Match(c => c.DeclaredValues, o => o.OrderedValues);

    /// <summary>
    /// The editor control this Dimension derives, from its algebra alone — never authored.
    /// Categorical axes offer a multi-select; ordinal axes offer a slider, with a
    /// "leave at the default" control only when the axis actually declares a default.
    /// Categorical axes declare NO defaults on either side — absence is the empty set, and the
    /// empty set already does everything a default was doing — so they always get a plain
    /// multi-select. An ordinal axis's "leave at the default" control appears only when that
    /// axis actually declares a default — a slider has no position for "unset", so the picker
    /// needs an explicit control to distinguish it from "deliberately set to the default".
    /// Duration declares no default (its absence <em>is</em> `Unprocessed`), so it derives no
    /// such control.
    /// </summary>
    public ControlShape ControlShape => Match<ControlShape>(
        _ => new MultiSelect(),
        o => new Slider(HasLeaveAtDefault: o.TaskDefault is not null));
}

/// <summary>
/// The shape of a Dimension's editor control, derived from its algebra (never configured —
/// see Constraint 7). A categorical axis is always a multi-select; an ordinal axis is always
/// a slider, one that additionally exposes a "leave at the default" control when — and only
/// when — the axis declares a default on that side. Duration declares none, so it has none.
/// </summary>
[GenerateOneOf]
public partial class ControlShape : OneOfBase<MultiSelect, Slider>;

public sealed record MultiSelect;

public sealed record Slider(bool HasLeaveAtDefault);

/// <summary>Location, With whom, Weather. Both sides carry a set; matching is subset.</summary>
public sealed record CategoricalDimension(
    DimensionId Id,
    string Label,
    IReadOnlyList<TagValue> DeclaredValues,
    WindowValueSource WindowSource = WindowValueSource.Authored)
{
    /// <summary><see cref="DeclaredValues"/> compares as a multiset — both sides carry a set,
    /// and matching is subset.</summary>
    public bool Equals(CategoricalDimension? other) =>
        other is not null
        && Id.Equals(other.Id)
        && Label == other.Label
        && WindowSource == other.WindowSource
        && StructuralEquality.MultisetEqual(DeclaredValues, other.DeclaredValues);

    public override int GetHashCode() =>
        HashCode.Combine(Id, Label, WindowSource, StructuralEquality.MultisetHash(DeclaredValues));
}

/// <summary>Mental energy, Duration. One value per side; the Window's is a ceiling.</summary>
public sealed record OrdinalDimension(
    DimensionId Id,
    string Label,
    IReadOnlyList<TagValue> OrderedValues,
    TagValue? TaskDefault,
    TagValue? WindowDefault,
    WindowValueSource WindowSource = WindowValueSource.Authored)
{
    public int RankOf(TagValue value)
    {
        for (var i = 0; i < OrderedValues.Count; i++)
        {
            if (OrderedValues[i] == value) return i;
        }

        return -1;
    }

    /// <summary><see cref="OrderedValues"/> compares as a sequence — <see cref="RankOf"/> returns
    /// the index, so a reorder is a different axis.</summary>
    public bool Equals(OrdinalDimension? other) =>
        other is not null
        && Id.Equals(other.Id)
        && Label == other.Label
        && WindowSource == other.WindowSource
        && TaskDefault == other.TaskDefault
        && WindowDefault == other.WindowDefault
        && StructuralEquality.SequenceEqual(OrderedValues, other.OrderedValues);

    public override int GetHashCode() =>
        HashCode.Combine(
            Id,
            Label,
            WindowSource,
            TaskDefault,
            WindowDefault,
            StructuralEquality.SequenceHash(OrderedValues));
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
