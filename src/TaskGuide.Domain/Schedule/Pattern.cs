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
}

/// <summary>
/// `patterns.json`'s envelope. "Which Pattern is active" is the only singleton fact in the
/// store, and it is a property of this collection — which is why there is no `settings.json`.
/// </summary>
public sealed record PatternBook(PatternId ActivePatternId, IReadOnlyList<Pattern> Patterns)
{
    public Pattern Active => Patterns.Single(p => p.Id == ActivePatternId);
}
