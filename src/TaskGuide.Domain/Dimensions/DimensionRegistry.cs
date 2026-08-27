namespace TaskGuide.Domain.Dimensions;

/// <summary>
/// The declared set of Dimensions. Code, never storage (#23) — asserted at startup.
/// </summary>
/// <remarks>
/// A value name belongs to exactly one Dimension across the whole registry. A registry that
/// declares the same value twice is a <em>defect</em>: it is rejected before the service will
/// run rather than resolved at the point of use. That refusal is a crash loop that pushes
/// nothing, so it signals failure outbound — carrying the duplicate value — before exiting,
/// per Liveness.
/// </remarks>
public sealed record DimensionRegistry(IReadOnlyList<Dimension> Dimensions)
{
    public Dimension? Claiming(Tags.TagValue value) => throw new NotImplementedException();

    /// <summary>Throws <see cref="DuplicateDimensionValueException"/>; never returns false.</summary>
    public void AssertNoDuplicateValues() => throw new NotImplementedException();
}

public sealed class DuplicateDimensionValueException(string value, IReadOnlyList<DimensionId> claimedBy)
    : Exception($"Dimension value '{value}' is declared by {claimedBy.Count} Dimensions")
{
    public string Value { get; } = value;
    public IReadOnlyList<DimensionId> ClaimedBy { get; } = claimedBy;
}
