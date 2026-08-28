using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Matching;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.Domain.Time;

namespace TaskGuide.Domain.Ranking;

/// <summary>
/// How many Availability Windows <em>ahead of now</em> would admit this Task. A plain count, and
/// the input Scarcity ranks on — the two words stay separate because more Opportunities is
/// better for a Task and worse for its rank.
/// </summary>
public sealed class OpportunityCounter(
    IDayShapeReader shapes,
    DimensionRegistry registry,
    ClockTimeResolution resolution,
    DayBoundary boundary)
{
    private readonly IDayShapeReader _shapes = shapes;
    private readonly DimensionRegistry _registry = registry;
    private readonly ClockTimeResolution _resolution = resolution;
    private readonly DayBoundary _boundary = boundary;

    /// <summary>A true rolling 7 x 24h, measured from now — not the Pattern's Sun-Sat week.</summary>
    private static readonly TimeSpan RollingHorizon = TimeSpan.FromDays(7);


    /// <summary>
    /// A rolling <c>min(7 days, time to Deadline)</c> measured from now — not the Pattern's
    /// Sun–Sat week. Once the Deadline has passed the bound is <b>dropped</b> and the horizon
    /// reverts to a plain rolling 7 days; without that it goes negative and every overdue Task
    /// misreports as an Orphan.
    /// </summary>
    public int CountAhead(TaskItem task, DateTimeOffset now)
    {
        var horizonEnd = HorizonEnd(task.Deadline, now);

        return WindowsOn(DatesFrom(_boundary.DateOf(now), _boundary.DateOf(horizonEnd)), _shapes)
            .Count(slot => FallsWithin(slot, now, horizonEnd) && Admits(task, slot, NoFetchedValues));
    }

    /// <summary>
    /// The second count, and the one that tells the two kinds of zero apart: could any Window in
    /// the <em>active Pattern</em> ever admit this Task? Defined for every Task whether or not it
    /// is currently eligible, which is why an absent value is not a zero.
    /// </summary>
    public int CountInPatternWeek(
        TaskItem task,
        Pattern pattern,
        IReadOnlyList<DayTemplate> templates,
        DateOnly weekOf) =>
        DatesFrom(weekOf, weekOf.AddDays(6))
            .SelectMany(date => TemplateOn(pattern, templates, date).Windows.Select(window => (date, window)))
            .Count(slot => Admits(task, slot, EveryFetchedValue));

    /// <summary>
    /// <c>min(7 days, time to Deadline)</c>, and both halves are load-bearing. A Deadline whose
    /// day is already over is <b>dropped</b> rather than clamped: clamping to zero would report
    /// every overdue Task as having no Opportunity at all, which reads as an Orphan.
    /// </summary>
    private DateTimeOffset HorizonEnd(DateOnly? deadline, DateTimeOffset now)
    {
        var rolling = now + RollingHorizon;
        if (deadline is null) return rolling;

        // The end of the Deadline day, so the number reads "3 chances before it is due" — not
        // the same clock time on it, which would silently drop that day's own Windows.
        var dueBy = _boundary.EndOf(deadline.Value);

        return dueBy <= now || dueBy > rolling ? rolling : dueBy;
    }

    /// <summary>
    /// Not yet over, and starting inside the horizon. The two edges deliberately read different
    /// ends of the Window: a Window you are <em>standing in</em> is a chance you can still take —
    /// <c>SnoozePolicy.CeilingFor</c> already says as much in code, re-deriving the Duration
    /// ceiling from the time <em>actually remaining</em> — and the landing page a notification
    /// opens is read inside a running Window by construction, so "3 chances before it is due" was
    /// off by one exactly when it is read most. The far edge stays on the start and stays
    /// half-open, which is what makes a once-a-week opportunity count exactly once.
    /// </summary>
    private bool FallsWithin((DateOnly Date, AvailabilityWindow Window) slot, DateTimeOffset now, DateTimeOffset horizonEnd)
    {
        var start = _resolution.Resolve(slot.Date, slot.Window.Start);
        var end = _resolution.Resolve(slot.Date, slot.Window.End);

        return end > now && start < horizonEnd;
    }

    /// <summary>
    /// Would this Window admit this Task on this date? A Window whose resolved length is zero —
    /// the spring-gap case — is no opportunity at all, so it never counts.
    /// </summary>
    private bool Admits(
        TaskItem task,
        (DateOnly Date, AvailabilityWindow Window) slot,
        IReadOnlyDictionary<DimensionId, IReadOnlyList<TagValue>> fetched) =>
        _resolution.LengthOf(slot.Date, slot.Window.Start, slot.Window.End) > TimeSpan.Zero
        && Matcher.Fits(task, ContextFor(slot.Date, slot.Window, fetched), _registry);

    private MatchContext ContextFor(
        DateOnly date,
        AvailabilityWindow window,
        IReadOnlyDictionary<DimensionId, IReadOnlyList<TagValue>> fetched) => new(
        window,
        DurationBuckets is { Count: > 0 } buckets ? window.DurationCeiling(date, _resolution, buckets) : default,
        fetched,
        Array.Empty<DimensionId>());

    /// <summary>
    /// A future Window's fetched axes are simply not known, and unknown resolves to the empty
    /// set — the same fail-closed rule absence already follows. This is the right rule for
    /// <em>"will this fire?"</em>, which is the question <see cref="CountAhead"/> asks.
    /// </summary>
    private static IReadOnlyDictionary<DimensionId, IReadOnlyList<TagValue>> NoFetchedValues { get; } =
        new Dictionary<DimensionId, IReadOnlyList<TagValue>>();

    /// <summary>
    /// Every value a fetched axis declares, so that axis constrains nothing. The Pattern-week
    /// count asks whether any Window could <b>ever</b> admit this Task — an explicitly
    /// counterfactual, structural question, in which a live condition is not a constraint at all.
    /// Failing it closed here would report every weather-tagged Task as an Orphan, sending the
    /// user to declare a Tag on a Dimension whose window side is blank by design.
    /// </summary>
    private IReadOnlyDictionary<DimensionId, IReadOnlyList<TagValue>> EveryFetchedValue =>
        _registry.Dimensions
            .OfType<CategoricalDimension>()
            .Where(dimension => dimension.WindowSource == WindowValueSource.Fetched)
            .ToDictionary(dimension => dimension.Id, dimension => dimension.DeclaredValues);

    /// <summary>
    /// The ordinal axis whose window-side value is derived from the Window's length — read off
    /// the registry's algebra, the same discriminator <c>Matcher</c> uses, never off a static.
    /// </summary>
    private IReadOnlyList<TagValue> DurationBuckets => _registry.Dimensions
        .OfType<OrdinalDimension>()
        .SingleOrDefault(dimension => dimension.WindowSource == WindowValueSource.Derived)
        ?.OrderedValues ?? Array.Empty<TagValue>();

    /// <summary>The shape each real date actually has — Overrides and Events already applied.</summary>
    private static IEnumerable<(DateOnly Date, AvailabilityWindow Window)> WindowsOn(
        IEnumerable<DateOnly> dates,
        IDayShapeReader shapes) =>
        dates.SelectMany(date => shapes.For(date).Windows.Select(window => (date, window)));

    /// <summary>
    /// A Pattern is an assumption, not a calendar: its weekday's Day template is read straight
    /// through, so no Override and no Event can reach this count. A Pattern referencing a
    /// template that is not there is a defect, not a zero.
    /// </summary>
    private static DayTemplate TemplateOn(Pattern pattern, IReadOnlyList<DayTemplate> templates, DateOnly date) =>
        templates.Single(template => template.Id == pattern[date.DayOfWeek]);

    private static IEnumerable<DateOnly> DatesFrom(DateOnly first, DateOnly last)
    {
        for (var date = first; date <= last; date = date.AddDays(1))
        {
            yield return date;
        }
    }
}

/// <summary>
/// Two zeroes that look identical and mean opposite things.
/// </summary>
public enum ZeroKind
{
    /// <summary>
    /// No Window in the active Pattern can <em>ever</em> admit it — something is malformed.
    /// Categorically worse than `Unprocessed` or `Stale`, so it gets a badge and a third,
    /// disjoint footer count. The repair is always "declare this Tag on some Window", so the
    /// badge deep-links into the window editor, pre-filtered to the <em>active</em> Pattern's
    /// distinct Day templates that don't yet declare a value on that Tag's Dimension.
    /// </summary>
    Orphan,

    /// <summary>
    /// Normally doable, but every admitting Window falls outside the horizon — usually because
    /// an Override or Event displaced it. Nothing is wrong.
    /// </summary>
    NoneInThisStretch,
}

public static class OrphanDetection
{
    /// <summary>
    /// <b>Respects the Status gate and ignores the clock gates.</b> Only an `Active` Task can be
    /// an Orphan; Defer and Postpone are deliberately not consulted, because orphan-ness asks
    /// whether any Window could <em>ever</em> admit the Task. Tasks only — Events are never
    /// matched — and derived Tasks are included, since an orphaned one means a badly written rule.
    /// </summary>
    public static bool IsOrphan(TaskItem task, Status status, int patternWeekCount) =>
        status == Status.Active && patternWeekCount == 0;

    /// <summary>
    /// Which of the two zeroes this one is, told apart by the Pattern-week count — or
    /// <c>null</c> where there is no zero to read. Two cases return null and they are not the
    /// same case: <b>Opportunities of 1 or more is not a zero</b> (a near-orphan gets no badge;
    /// Ranking already surfaces it), and a Task the Status gate excludes has <b>no
    /// Opportunities value at all</b> — an absence, not a zero.
    /// </summary>
    public static ZeroKind? KindOfZero(TaskItem task, Status status, int opportunities, int patternWeekCount) =>
        status != Status.Active || opportunities != 0
            ? null
            : IsOrphan(task, status, patternWeekCount) ? ZeroKind.Orphan : ZeroKind.NoneInThisStretch;
}
