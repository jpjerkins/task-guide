using OneOf;
using TaskGuide.Application.Firing;
using TaskGuide.Application.Ports;
using TaskGuide.Application.Rules;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Matching;
using TaskGuide.Domain.Notifications;
using TaskGuide.Domain.Ranking;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.Domain.Time;

namespace TaskGuide.Application.Reminders;

/// <summary>
/// The one rule behind "why did the disabled control say that", shared by the two places that
/// need it: the endpoint that renders a Snooze control disabled, and the endpoint that rejects
/// the POST that control would otherwise send. A rejection must render the identical line the
/// disabled state already showed.
/// </summary>
public static class ReminderSuppression
{
    public const string StaleDay = "This reminder was for yesterday";
    public const string SnoozeEndsAtMidnight = "Snooze ends at midnight";

    /// <summary>null when Snooze is offered.</summary>
    public static string? SnoozeLine(DateTimeOffset now, TimeSpan interval, DateTimeOffset dayBoundary) =>
        SnoozePolicy.IsSnoozeOffered(now, interval, dayBoundary)
            ? null
            : now >= dayBoundary ? StaleDay : SnoozeEndsAtMidnight;

    /// <summary>
    /// The page-level gate: not "can this control still do something" but "is this page still
    /// about a live day". Snooze and "Matching on" read this; mark off and Postpone do not.
    /// </summary>
    public static string? PageLine(DateTimeOffset now, DateTimeOffset dayBoundary) =>
        now >= dayBoundary ? StaleDay : null;
}

/// <summary>
/// The read behind the landing page a Pushover notification opens. Pushover carries one URL and
/// nothing actionable, so every control on the reminder lives here — the response must let the
/// SPA render without a client-side clock and without deriving anything.
/// </summary>
public sealed class ReadReminderPage(
    IStoreReader store,
    IDayShapeReader shapes,
    DimensionRegistry registry,
    ClockTimeResolution resolution,
    DayBoundary boundary,
    StaleThresholds staleThresholds,
    DerivedTaskComposer derivedTasks,
    TickPlanner planner,
    IWeatherSource weather,
    TimeProvider timeProvider)
{
    private const string FallbackKey = "fallback";

    private static readonly IReadOnlyDictionary<DimensionId, IReadOnlyList<TagValue>> NoFetched =
        new Dictionary<DimensionId, IReadOnlyList<TagValue>>();

    private static readonly MatchingOnAxes NoAxes = new(NoFetched, NoFetched);

    public async Task<ReminderPageOutcome> ExecuteAsync(DateOnly date, string windowKey, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var view = store.Read();
        var isFallback = windowKey == FallbackKey;

        var fireRow = (isFallback
                ? view.FiresOn(date).Rows.Where(row => row.Kind == FireKind.Fallback && row.IsFired)
                : view.FiresOn(date).Rows.Where(row =>
                    row.WindowId is not null && row.WindowId.Equals(new WindowId(windowKey)) && row.IsFired))
            .OrderByDescending(row => row.FiredAt)
            .FirstOrDefault();

        var shapeWindow = isFallback
            ? null
            : shapes.For(date).Windows.FirstOrDefault(window => window.Id.Equals(new WindowId(windowKey)));

        if (fireRow is null && shapeWindow is null)
        {
            return new ReminderNotFound();
        }

        var dayBoundary = boundary.EndOf(date);
        var staleLine = ReminderSuppression.PageLine(now, dayBoundary);
        var (fetched, failedFetches) = await FetchWeatherAsync(view, now, cancellationToken);

        ReminderPageContext context;
        var matches = (IReadOnlyList<TaskItem>)[];
        var matchingOn = NoAxes;

        if (isFallback)
        {
            var eventName = fireRow?.Carried is { } eventId
                ? view.Events.FirstOrDefault(candidate => candidate.Id.Equals(eventId))?.Name ?? ""
                : "";
            context = new FallbackContext(eventName);
        }
        else
        {
            var start = fireRow?.WindowStart ?? shapeWindow?.Start;
            var end = fireRow?.WindowEnd ?? shapeWindow?.End;
            if (start is not { } windowStart || end is not { } windowEnd)
            {
                // A row with no span and no Window left in the shape to read one from: there is
                // no span to derive a Snooze interval from and no Window page to render.
                return new ReminderNotFound();
            }

            var name = fireRow?.WindowName ?? shapeWindow?.Name ?? "";
            var interval = SnoozePolicy.IntervalFor(windowEnd.ToTimeSpan() - windowStart.ToTimeSpan());

            if (shapeWindow is not null)
            {
                (matches, matchingOn) = MatchesFor(view, shapeWindow, date, now, fetched, failedFetches);
            }

            context = new WindowContext(
                name, windowStart, windowEnd,
                (int)interval.TotalMinutes,
                ReminderSuppression.SnoozeLine(now, interval, dayBoundary));
        }

        return new ReminderPage(
            date,
            context,
            fireRow?.Kind,
            matches,
            IsLive: staleLine is null,
            staleLine,
            matchingOn,
            Footer(view, now),
            failedFetches);
    }

    /// <summary>
    /// Duration's ceiling is re-derived from the time actually remaining — the same rule a Snooze
    /// re-fire applies — never from the Window's authored length, which would offer a Task longer
    /// than what is actually left in the span.
    /// </summary>
    private (IReadOnlyList<TaskItem>, MatchingOnAxes) MatchesFor(
        IStoreView view,
        AvailabilityWindow window,
        DateOnly date,
        DateTimeOffset now,
        IReadOnlyDictionary<DimensionId, IReadOnlyList<TagValue>> fetched,
        IReadOnlyList<DimensionId> failedFetches)
    {
        var resolved = resolution.ResolveWindow(date, window);
        if (resolved is null)
        {
            return ([], NoAxes);
        }

        var duration = registry.Dimensions
            .Select(dimension => dimension.Value)
            .OfType<OrdinalDimension>()
            .Single(dimension => dimension.Id == KnownDimensions.Duration);
        var ceiling = SnoozePolicy.CeilingFor(resolved.End - now, duration.OrderedValues);
        var matchContext = new MatchContext(window, ceiling, fetched, failedFetches);
        var tasks = derivedTasks.Compose(view);

        var eligible = tasks
            .Where(task => StatusRules.IsEligible(task, view.CompletionsFor(task.Id), registry, staleThresholds, now, boundary))
            .Where(task => Matcher.Fits(task, matchContext, registry))
            .ToList();

        var counter = new OpportunityCounter(shapes, registry, resolution, boundary);
        var ranked = Ranker.Rank(eligible.Select(task => (
            task,
            Ranker.KeyFor(task, counter.CountAhead(task, now, fetched, failedFetches), registry, now, boundary))).ToList()).ToArray();

        return (ranked, MatchingOnFor(window, ceiling, fetched));
    }

    /// <summary>
    /// The split "Matching on" writes straight through to: declared is what the Window's own Tags
    /// carry, defaulted is every other Dimension's effective window-side value — the rule
    /// <see cref="Matcher"/> itself compares against.
    /// </summary>
    private MatchingOnAxes MatchingOnFor(
        AvailabilityWindow window,
        TagValue durationCeiling,
        IReadOnlyDictionary<DimensionId, IReadOnlyList<TagValue>> fetched)
    {
        var declared = new Dictionary<DimensionId, IReadOnlyList<TagValue>>();
        var defaulted = new Dictionary<DimensionId, IReadOnlyList<TagValue>>();

        foreach (var dimension in registry.Dimensions)
        {
            // A Derived or Fetched axis is the window-side default by construction — Matcher
            // never reads its window-side value off Tags (WindowCategoricalValues /
            // WindowOrdinalValue), so it can never be "declared" whatever the Tags hold.
            var declaredValues = dimension.Match(
                categorical => categorical.WindowSource == WindowValueSource.Fetched
                    ? (IReadOnlyList<TagValue>)[]
                    : window.Tags.On(categorical.Id),
                ordinal => ordinal.WindowSource == WindowValueSource.Derived
                    ? (IReadOnlyList<TagValue>)[]
                    : window.Tags.On(ordinal.Id));

            if (declaredValues.Count > 0)
            {
                declared[dimension.Id] = declaredValues;
                continue;
            }

            defaulted[dimension.Id] = dimension.Match(
                categorical => categorical.WindowSource == WindowValueSource.Fetched
                    ? fetched.TryGetValue(categorical.Id, out var values) ? values : (IReadOnlyList<TagValue>)[]
                    : [],
                ordinal => ordinal.WindowSource == WindowValueSource.Derived
                    ? [durationCeiling]
                    : ordinal.WindowDefault is { } windowDefault ? [windowDefault] : (IReadOnlyList<TagValue>)[]);
        }

        return new MatchingOnAxes(declared, defaulted);
    }

    private FooterCounts Footer(IStoreView view, DateTimeOffset now)
    {
        var counter = new OpportunityCounter(shapes, registry, resolution, boundary);
        var statuses = view.Tasks.Select(task => (task, status: StatusRules.Of(
            task, view.CompletionsFor(task.Id), registry, staleThresholds, now, boundary))).ToArray();
        var weekOf = boundary.DateOf(now).AddDays(-(int)boundary.DateOf(now).DayOfWeek);

        return new FooterCounts(
            statuses.Count(entry => entry.status == Status.Unprocessed),
            statuses.Count(entry => entry.status == Status.Stale),
            statuses.Count(entry => entry.status == Status.Active
                && OrphanDetection.IsTaskOrphan(
                    entry.status,
                    counter.CountInPatternWeek(entry.task, view.Patterns.Active, view.DayTemplates, weekOf))));
    }

    private async Task<(IReadOnlyDictionary<DimensionId, IReadOnlyList<TagValue>> Fetched, IReadOnlyList<DimensionId> Failed)> FetchWeatherAsync(
        IStoreView view, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!planner.NeedsWeather(view, now))
        {
            return (NoFetched, []);
        }

        var outcome = await weather.CurrentAsync(cancellationToken);
        return outcome.Match(
            known => ((IReadOnlyDictionary<DimensionId, IReadOnlyList<TagValue>>)new Dictionary<DimensionId, IReadOnlyList<TagValue>>
            {
                [KnownDimensions.Weather] = known.Value,
            }, (IReadOnlyList<DimensionId>)[]),
            unavailable => (NoFetched, (IReadOnlyList<DimensionId>)[KnownDimensions.Weather]));
    }
}

/// <summary>The Window's own name and span, its Snooze offer, and the line suppressing it.</summary>
public sealed record WindowContext(
    string Name, TimeOnly Start, TimeOnly End,
    int SnoozeIntervalMinutes, string? SnoozeSuppression);

/// <summary>
/// The one push with no Window behind it — no span to derive an interval from, no Dimension
/// values to re-match against, so no Snooze can be constructed here.
/// </summary>
public sealed record FallbackContext(string EventName);

[GenerateOneOf]
public partial class ReminderPageContext : OneOfBase<WindowContext, FallbackContext>;

public sealed record MatchingOnAxes(
    IReadOnlyDictionary<DimensionId, IReadOnlyList<TagValue>> Declared,
    IReadOnlyDictionary<DimensionId, IReadOnlyList<TagValue>> Defaulted);

public sealed record ReminderPage(
    DateOnly Date,
    ReminderPageContext Context,
    FireKind? FiredAs,
    IReadOnlyList<TaskItem> Matches,
    bool IsLive,
    string? StaleLine,
    MatchingOnAxes MatchingOn,
    FooterCounts Footer,
    IReadOnlyList<DimensionId> FailedFetches);

[GenerateOneOf]
public partial class ReminderPageOutcome : OneOfBase<ReminderPage, ReminderNotFound>;
