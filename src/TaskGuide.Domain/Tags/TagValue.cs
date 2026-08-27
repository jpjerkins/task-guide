namespace TaskGuide.Domain.Tags;

/// <summary>
/// A value belonging to exactly one Dimension across the whole registry. Lowercase — a phone
/// capitalises a dictated Tag whether or not you meant it to. Because value names are globally
/// unique, a Tag is a plain string and nothing has to be qualified in storage (#21).
/// </summary>
public readonly record struct TagValue
{
    public TagValue(string value) => Value = value.ToLowerInvariant();
    public string Value { get; }
    public override string ToString() => Value;
}

/// <summary>
/// A free string belonging to no Dimension: kept, visible, and <em>inert to matching</em>.
/// Inert is a statement about the registry, not about the Tag — the string is never lost or
/// rewritten, so inertness is reversible in both directions.
/// </summary>
public readonly record struct LooseTag
{
    public LooseTag(string value) => Value = value.ToLowerInvariant();
    public string Value { get; }
    public override string ToString() => Value;
}
