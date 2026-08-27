namespace TaskGuide.Domain.Dimensions;

/// <summary>
/// An axis that both a Task and an Availability Window carry a value on — the only thing
/// matching looks at. Declared in code; there is no UI for managing Dimensions.
/// Identity and label are separate, so a rename touches no stored data.
/// </summary>
public abstract record Dimension(DimensionId Id, string Label)
{
    public abstract IReadOnlyList<TagValue> Values { get; }
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

    public int RankOf(TagValue value) => OrderedValues.IndexOf(value);
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
