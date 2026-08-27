using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tasks;

namespace TaskGuide.Domain.Rules;

/// <summary>
/// A rule that reads a <b>dated record</b> and produces an obligation carrying its own Deadline.
/// Neither filter nor rank — a third mechanism. <b>A rule is an assumption; the dated record is
/// the fact</b>, and derived obligations are computed on read and never stored, which is what
/// makes the mechanism nearly free: a derived obligation has no lifecycle.
/// </summary>
/// <remarks>
/// Written with the open/closed principle in mind, and with <b>no management UI</b> — new
/// behaviour arrives as a new rule, not as configuration. Each rule hard-codes the shape of the
/// Task it produces; <b>Tags are never inherited from the trigger</b>, which is the one default
/// that can silently manufacture Orphans.
/// </remarks>
public interface IDerivedObligationRule
{
    RuleId Id { get; }

    /// <summary>
    /// Triggers are found by scanning the sparse stored records — dated Events and Overrides —
    /// never by walking a calendar. Recurring Events never trigger: a weekly commitment would
    /// derive one every week forever.
    /// </summary>
    IEnumerable<TaskItem> Derive(DerivedObligationContext context);
}

public sealed record DerivedObligationContext(
    DateOnly Today,
    IReadOnlyList<Event> DatedEvents,
    IReadOnlyList<DateOverride> Overrides,
    IDayShapeReader Shapes,
    IReadOnlyList<DerivedCompletionEntry> Completions);

/// <summary>
/// The generic rule, no Tag involved: the active Pattern assumes Event E on this date, E declares
/// an Absence notice, and the date's actual shape does not contain E → "Tell E you'll be out",
/// due <c>E's date − Absence notice</c>.
/// </summary>
/// <remarks>
/// <b>Absence, not overlap.</b> An Event that merely overlaps the commitment does not trigger it.
/// The date's shape lacks E in exactly two ways: an Override stamped a day without it, or that
/// date's instance was deleted (an <see cref="EventException"/>). Contiguous absences coalesce —
/// a trip spanning three Sundays derives one obligation, due before the first.
/// <para>
/// Weekday-and-time literals were rejected: the Pattern already owns that knowledge, and a
/// hard-coded weekday goes silently wrong the day karate moves. Joining a third standing
/// commitment is therefore authoring, not a code change.
/// </para>
/// </remarks>
public sealed class AbsenceRule : IDerivedObligationRule
{
    public RuleId Id => new("absence");

    public IEnumerable<TaskItem> Derive(DerivedObligationContext context) => throw new NotImplementedException();
}

/// <summary>
/// Tag-declared family: reads a Tag on a dated Event and produces its Task with an Offset
/// deadline — <c>#timeoff</c>, <c>#planetickets</c>, <c>#placetostay</c>. Each is a small rule in
/// code; each words its Task differently, so there is nothing to generalise.
/// </summary>
public sealed class TagDeclaredRule(RuleId id, string tag) : IDerivedObligationRule
{
    public RuleId Id => id;
    public string Tag => tag;

    public IEnumerable<TaskItem> Derive(DerivedObligationContext context) => throw new NotImplementedException();
}
