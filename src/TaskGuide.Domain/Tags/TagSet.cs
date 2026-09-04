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

    /// <summary>
    /// A positional record's synthesised equality compares <see cref="Dimensions"/> and
    /// <see cref="LooseTags"/> by <b>reference</b> (a dictionary and a list are both reference
    /// types), so two structurally identical <see cref="TagSet"/>s built from freshly-constructed
    /// collections would compare unequal — the #69/ADR-0011 watch-budget bug, arriving through
    /// this door instead of <c>GlanceState</c>'s. Equality here follows the Tag model
    /// (`CONTEXT.md` § Tag), not convenience: a Dimension's values are a <b>set</b> — order does
    /// not matter — and the loose Tags are a <b>bag</b>, also order-insensitive; both stay
    /// duplicate-count-sensitive, since a stray duplicate is a real (if unintended) difference,
    /// not noise to absorb. A Dimension key mapped to an empty list equals that key being
    /// absent, mirroring <see cref="On"/>'s "absence is the empty set" rule.
    /// </summary>
    public bool Equals(TagSet? other)
    {
        if (other is null) return false;

        if (!MultisetEqual(LooseTags, other.LooseTags)) return false;

        var mine = Dimensions.Where(kv => kv.Value.Count > 0).ToArray();
        var theirs = other.Dimensions.Where(kv => kv.Value.Count > 0).ToArray();
        if (mine.Length != theirs.Length) return false;

        foreach (var (id, values) in mine)
        {
            if (!other.Dimensions.TryGetValue(id, out var otherValues)) return false;
            if (!MultisetEqual(values, otherValues)) return false;
        }

        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(MultisetHash(LooseTags));
        foreach (var (id, values) in Dimensions)
        {
            if (values.Count == 0) continue;
            unchecked
            {
                hash.Add(id.GetHashCode() + MultisetHash(values));
            }
        }
        return hash.ToHashCode();
    }

    /// <summary>Order-insensitive, duplicate-count-sensitive comparison — a multiset, not a set.</summary>
    private static bool MultisetEqual<T>(IReadOnlyList<T> a, IReadOnlyList<T> b)
    {
        if (a.Count != b.Count) return false;
        var sortedA = a.OrderBy(v => v!.ToString(), StringComparer.Ordinal).ToArray();
        var sortedB = b.OrderBy(v => v!.ToString(), StringComparer.Ordinal).ToArray();
        return sortedA.SequenceEqual(sortedB);
    }

    /// <summary>An unchecked sum of element hashes is order-free and duplicate-count-sensitive.</summary>
    private static int MultisetHash<T>(IReadOnlyList<T> values)
    {
        var sum = 0;
        unchecked
        {
            foreach (var value in values) sum += value!.GetHashCode();
        }
        return sum;
    }
}
