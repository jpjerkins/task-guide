using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.Domain.Time;

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

/// <summary>
/// The one shape every rule builds its Task through. Nothing here is stored: the id is a
/// function of the rule and the trigger, so the same obligation derived twice is the same
/// obligation, and there is no minting and no lifecycle.
/// </summary>
internal static class DerivedTask
{
    /// <summary>
    /// A rule's hard-coded Duration, read off the registry rather than written as a literal.
    /// A bucket that does not exist throws here instead of silently producing an `Unprocessed`
    /// obligation — which is the one failure this mechanism cannot tolerate.
    /// </summary>
    internal static TagValue Bucket(string minutes) =>
        KnownDimensions.DurationBuckets.Single(known => known.Value == minutes);

    internal static TaskItem From(
        DerivedObligationContext context,
        RuleId rule,
        string triggerId,
        string title,
        DateOnly due,
        TagValue duration) =>
        new(
            new TaskId($"t_derived_{rule.Value}_{triggerId}"),
            title,
            Notes: null,
            // Hard-coded, never inherited — and it always carries a Duration, which is the only
            // reason a derived Task is never `Unprocessed`.
            Tags: new TagSet(
                new Dictionary<DimensionId, IReadOnlyList<TagValue>>
                {
                    [KnownDimensions.Duration] = [duration],
                },
                Array.Empty<LooseTag>()),
            Deadline: due,
            Defer: null,
            Postpone: null,
            Recurrence: null,
            // A derived Task has no authored creation instant; the day its Deadline falls on is
            // the only date it owns. Nothing reads it but Ranking's final tiebreak.
            CreatedAt: context.Boundary.EndOf(due.AddDays(-1)))
        {
            Provenance = new DerivedProvenance(rule, triggerId),
        };

    /// <summary>
    /// The completion key is `{ ruleId, triggerId, due }`. Keying on `due` earns a case for
    /// free: move the trigger and the logged `due` no longer matches the live one.
    /// </summary>
    internal static bool IsDone(
        DerivedObligationContext context, RuleId rule, string triggerId, DateOnly due) =>
        context.Completions.Any(entry =>
            entry.RuleId == rule && entry.TriggerId == triggerId && entry.Due == due);
}

public sealed record DerivedObligationContext(
    DateOnly Today,
    IReadOnlyList<Event> DatedEvents,
    IReadOnlyList<DateOverride> Overrides,
    IDayShapeReader Shapes,
    IReadOnlyList<DerivedCompletionEntry> Completions)
{
    /// <summary>
    /// The assumption side of the absence rule. A Pattern is never reified, so "does the active
    /// Pattern assume Event E on this date" is answered from the book and the templates it
    /// references — read, never walked.
    /// </summary>
    public PatternBook? Patterns { get; init; }

    public IReadOnlyList<DayTemplate> DayTemplates { get; init; } = Array.Empty<DayTemplate>();

    /// <summary>
    /// The second of the exactly two ways a date's shape lacks E. Sparse and stored, like the
    /// Overrides beside it.
    /// </summary>
    public IReadOnlyList<EventException> EventExceptions { get; init; } = Array.Empty<EventException>();

    /// <summary>Nothing in the model may disagree about what day it is.</summary>
    public DayBoundary Boundary { get; init; } = new(TimeZoneInfo.FindSystemTimeZoneById(DayBoundary.ZoneId));
}

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

    public IEnumerable<TaskItem> Derive(DerivedObligationContext context)
    {
        // The sparse stored records that can say a date's shape lacks E — an Override stamped
        // on it, or that date's instance deleted. Never a calendar walk.
        var candidates = context.Overrides
            .Select(stamped => stamped.Date)
            .Concat(context.EventExceptions.Where(exception => exception.Deleted).Select(e => e.Date))
            .Distinct()
            .Order();

        var absences = new List<(EventPrototype Commitment, DateOnly Date)>();

        foreach (var date in candidates)
        {
            foreach (var commitment in Assumed(context, date))
            {
                if (commitment.AbsenceNotice is not null && IsAbsent(context, date, commitment))
                {
                    absences.Add((commitment, date));
                }
            }
        }

        foreach (var run in Coalesce(context, absences))
        {
            var (commitment, first) = (run.Commitment, run.First);

            // Past the trigger's date it simply stops being derived: an obligation you can no
            // longer act on is not an obligation.
            if (first < context.Today)
            {
                continue;
            }

            var due = commitment.AbsenceNotice!.ResolveAgainst(first);
            var triggerId = TriggerId(commitment, first);

            if (DerivedTask.IsDone(context, Id, triggerId, due))
            {
                continue;
            }

            yield return DerivedTask.From(
                context, Id, triggerId, $"Tell {commitment.Name} you'll be out", due, DerivedTask.Bucket("10"));
        }
    }

    /// <summary>The trigger is this commitment's instance on this date, not the record that removed it.</summary>
    private static string TriggerId(EventPrototype commitment, DateOnly date) =>
        $"{commitment.Id.Value}@{date:yyyy-MM-dd}";

    /// <summary>
    /// What the active Pattern assumes on a date. Read off the book and the templates it
    /// references — the Pattern is never reified, so there is nothing dated to consult.
    /// </summary>
    private static IReadOnlyList<EventPrototype> Assumed(DerivedObligationContext context, DateOnly date)
    {
        if (context.Patterns is not { } book)
        {
            return Array.Empty<EventPrototype>();
        }

        var template = book.Active[date.DayOfWeek];

        return context.DayTemplates.FirstOrDefault(candidate => candidate.Id == template)?.EventPrototypes
            ?? Array.Empty<EventPrototype>();
    }

    /// <summary>
    /// <b>Absence, not overlap.</b> A deleted instance is absence outright; otherwise the
    /// question is only whether the date's actual shape still carries the commitment — a
    /// tournament starting at 10 leaves the 9:00 service on the shape, and a moved instance
    /// stays on it too.
    /// </summary>
    private static bool IsAbsent(DerivedObligationContext context, DateOnly date, EventPrototype commitment)
    {
        var exception = context.EventExceptions.FirstOrDefault(
            e => e.Date == date && e.PrototypeId == commitment.Id);

        if (exception is { Deleted: true })
        {
            return true;
        }

        var name = exception?.Name ?? commitment.Name;

        return !context.Shapes.For(date).Events.Any(actual => actual.Name == name);
    }

    /// <summary>
    /// Contiguous absences coalesce — a trip spanning three Sundays derives one obligation, due
    /// before the first. Contiguity is structural: two absences are contiguous when the Pattern
    /// assumes no further instance of the same commitment between them. No proximity threshold,
    /// therefore no knob.
    /// </summary>
    private static IEnumerable<(EventPrototype Commitment, DateOnly First)> Coalesce(
        DerivedObligationContext context,
        IReadOnlyList<(EventPrototype Commitment, DateOnly Date)> absences)
    {
        foreach (var group in absences.GroupBy(absence => absence.Commitment.Id))
        {
            var commitment = group.First().Commitment;
            var dates = group.Select(absence => absence.Date).Order().ToList();

            for (var i = 0; i < dates.Count; i++)
            {
                if (i > 0 && NothingAssumedBetween(context, commitment, dates[i - 1], dates[i]))
                {
                    continue;   // still inside the run that started earlier
                }

                yield return (commitment, dates[i]);
            }
        }
    }

    /// <summary>
    /// A bounded walk between two records already in hand, not a search: any assumed instance
    /// strictly between two absences is necessarily one that was <em>not</em> absent, and it
    /// breaks the run.
    /// </summary>
    /// <remarks>
    /// Day-by-day, and the two absences can in principle be a year apart — cheap in practice
    /// because both ends are records already in hand, and a run of absences is a trip.
    /// </remarks>
    private static bool NothingAssumedBetween(
        DerivedObligationContext context, EventPrototype commitment, DateOnly from, DateOnly to)
    {
        for (var date = from.AddDays(1); date < to; date = date.AddDays(1))
        {
            if (Assumed(context, date).Any(assumed => assumed.Id == commitment.Id))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Tag-declared family: reads a Tag on a dated Event and produces its Task with an Offset
/// deadline — <c>#timeoff</c>, <c>#planetickets</c>, <c>#placetostay</c>. Each is a small rule in
/// code; each words its Task differently, so there is nothing to generalise.
/// </summary>
/// <remarks>
/// <b>The constructor is private.</b> The members below are the whole family, so a rule cannot be
/// assembled from data at run time and the parameterisation stays an implementation detail rather
/// than a configuration surface. A fourth obligation is a fourth member here — a code change.
/// </remarks>
public sealed class TagDeclaredRule : IDerivedObligationRule
{
    private readonly RuleId _id;
    private readonly string _tag;
    private readonly string _title;
    private readonly Offset _lead;
    private readonly TagValue _duration;

    private TagDeclaredRule(RuleId id, string tag, string title, Offset lead, TagValue duration) =>
        (_id, _tag, _title, _lead, _duration) = (id, tag, title, lead, duration);

    /// <summary>Ask off work three weeks out — the first of the family, and the reason it exists.</summary>
    public static TagDeclaredRule TimeOff { get; } = new(
        new RuleId("timeoff"), "timeoff", "Ask off work",
        new BeforeOffset(3, OffsetUnit.Weeks), DerivedTask.Bucket("10"));

    public static TagDeclaredRule PlaneTickets { get; } = new(
        new RuleId("planetickets"), "planetickets", "Buy plane tickets",
        new BeforeOffset(2, OffsetUnit.Months), DerivedTask.Bucket("30"));

    public static TagDeclaredRule PlaceToStay { get; } = new(
        new RuleId("placetostay"), "placetostay", "Book a place to stay",
        new BeforeOffset(1, OffsetUnit.Months), DerivedTask.Bucket("30"));

    public RuleId Id => _id;

    /// <summary>The loose Tag on a dated Event that declares this obligation.</summary>
    public string Tag => _tag;

    /// <summary>The hard-coded shape of the Task this rule produces — never the trigger's.</summary>
    public string Title => _title;

    public Offset Lead => _lead;

    public TagValue Duration => _duration;

    public IEnumerable<TaskItem> Derive(DerivedObligationContext context)
    {
        var declared = new LooseTag(_tag);

        foreach (var trigger in context.DatedEvents)
        {
            // A recurring Event never triggers: it is not a stored dated record, so scanning
            // them rather than walking the calendar is the whole of that rule.
            if (!trigger.Tags.LooseTags.Contains(declared) || trigger.Date < context.Today)
            {
                continue;
            }

            var due = _lead.ResolveAgainst(trigger.Date);

            if (DerivedTask.IsDone(context, _id, trigger.Id.Value, due))
            {
                continue;
            }

            yield return DerivedTask.From(context, _id, trigger.Id.Value, _title, due, _duration);
        }
    }
}
