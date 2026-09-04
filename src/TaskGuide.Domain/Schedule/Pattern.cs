using TaskGuide.Domain.Common;

namespace TaskGuide.Domain.Schedule;

/// <summary>
/// Seven Day template references, one per weekday. Exactly one Pattern is active at a time; a
/// seasonal change is a switch of the active Pattern.
/// </summary>
/// <remarks>
/// References are <b>live</b> — editing a Day template propagates to every Pattern using it,
/// and that propagation is the point. The risk is handled by visibility, not prevention: the
/// template editor shows its usage list before saving.
/// <para>
/// <b>A Pattern is an assumption, not a calendar.</b> It is never reified into dated records;
/// any future date's shape is computed on demand, and the only stored dated records are the
/// sparse Overrides and Events layered on top.
/// </para>
/// </remarks>
public sealed record Pattern(PatternId Id, string Name, IReadOnlyList<DayTemplateId> Days)
{
    /// <summary>Indexed by <see cref="DayOfWeek"/>; always exactly seven.</summary>
    public DayTemplateId this[DayOfWeek weekday] => Days[(int)weekday];

    /// <summary><see cref="Days"/> compares as a sequence — seven weekday slots indexed
    /// positionally by <see cref="this[DayOfWeek]"/>, so order is the meaning.</summary>
    public bool Equals(Pattern? other) =>
        other is not null
        && Id.Equals(other.Id)
        && Name == other.Name
        && StructuralEquality.SequenceEqual(Days, other.Days);

    public override int GetHashCode() =>
        HashCode.Combine(Id, Name, StructuralEquality.SequenceHash(Days));
}

/// <summary>
/// `patterns.json`'s envelope. "Which Pattern is active" is the only singleton fact in the
/// store, and it is a property of this collection — which is why there is no `settings.json`.
/// </summary>
public sealed record PatternBook(PatternId ActivePatternId, IReadOnlyList<Pattern> Patterns)
{
    public Pattern Active => Patterns.SingleOrDefault(p => p.Id == ActivePatternId)
        ?? throw new InvalidOperationException(
            $"Active Pattern {ActivePatternId.Value} does not match any Pattern in the store.");

    /// <summary><see cref="Patterns"/> compares as a multiset — <see cref="Active"/> finds a
    /// Pattern by id, not by position.</summary>
    public bool Equals(PatternBook? other) =>
        other is not null
        && ActivePatternId.Equals(other.ActivePatternId)
        && StructuralEquality.MultisetEqual(Patterns, other.Patterns);

    public override int GetHashCode() =>
        HashCode.Combine(ActivePatternId, StructuralEquality.MultisetHash(Patterns));
}
