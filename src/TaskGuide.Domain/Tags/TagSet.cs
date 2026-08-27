using TaskGuide.Domain.Dimensions;

namespace TaskGuide.Domain.Tags;

/// <summary>
/// Every Task and Window carries Tags in two places: a value on each Dimension, and a loose bag.
/// Which place a Tag lands in is not a choice — a string the registry claims is a Dimension
/// value, and one it does not is loose. Storage mirrors the entry controls exactly, which is
/// what makes the startup promote/demote sweep a slot move rather than a string rewrite.
/// </summary>
public sealed record TagSet(
    IReadOnlyDictionary<DimensionId, IReadOnlyList<TagValue>> Dimensions,
    IReadOnlyList<LooseTag> LooseTags)
{
    public static TagSet Empty { get; } = new(
        new Dictionary<DimensionId, IReadOnlyList<TagValue>>(),
        Array.Empty<LooseTag>());

    /// <summary>Absence is the empty set — not a default (categorical), not an error.</summary>
    public IReadOnlyList<TagValue> On(DimensionId dimension) =>
        Dimensions.TryGetValue(dimension, out var values) ? values : Array.Empty<TagValue>();

    /// <summary>An ordinal axis carries exactly one value per side, or none.</summary>
    public TagValue? SingleOn(DimensionId dimension) =>
        On(dimension) is [var only] ? only : null;
}
