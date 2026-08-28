using TaskGuide.Domain.Tags;

namespace TaskGuide.Domain.Dimensions;

/// <summary>
/// The pure promote/demote sweep: reconciles a TagSet's Dimension slots and loose bag against
/// the registry's current declarations. Declaring a value promotes any loose Tag matching it
/// into that Dimension's slot; withdrawing a value demotes it back to the loose bag, string
/// intact. Pure — TagSet in, TagSet out. Wiring this to startup is a later, sequential task.
/// </summary>
public static class RegistrySweep
{
    public static TagSet Sweep(TagSet tagSet, DimensionRegistry registry)
    {
        var dimensions = new Dictionary<DimensionId, List<TagValue>>();
        var demoted = new List<LooseTag>();

        // Demote: a Dimension value the registry no longer declares under that Dimension
        // (withdrawn, or reassigned elsewhere) drops back to loose, string intact.
        foreach (var (dimensionId, values) in tagSet.Dimensions)
        {
            foreach (var value in values)
            {
                var claimant = registry.Claiming(value);
                if (claimant is not null && claimant.Id == dimensionId)
                {
                    Add(dimensions, dimensionId, value);
                }
                else
                {
                    demoted.Add(new LooseTag(value.Value));
                }
            }
        }

        // Promote: a loose Tag the registry now claims moves into that Dimension's slot —
        // except an ordinal axis that already carries a deliberately-set value, which a loose
        // Tag never overrules. Categorical axes are sets and have no such contest.
        var loose = new List<LooseTag>();
        foreach (var tag in tagSet.LooseTags.Concat(demoted))
        {
            var claimant = registry.Claiming(new TagValue(tag.Value));
            if (claimant is null)
            {
                loose.Add(tag);
                continue;
            }

            if (claimant is OrdinalDimension && dimensions.TryGetValue(claimant.Id, out var existing) && existing.Count > 0)
            {
                loose.Add(tag);
                continue;
            }

            Add(dimensions, claimant.Id, new TagValue(tag.Value));
        }

        return new TagSet(
            dimensions.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<TagValue>)kv.Value),
            loose);
    }

    private static void Add(Dictionary<DimensionId, List<TagValue>> dimensions, DimensionId id, TagValue value)
    {
        if (!dimensions.TryGetValue(id, out var list))
        {
            list = [];
            dimensions[id] = list;
        }

        if (!list.Contains(value))
        {
            list.Add(value);
        }
    }
}
