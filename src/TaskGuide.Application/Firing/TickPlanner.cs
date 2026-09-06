using TaskGuide.Application.Ports;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Matching;
using TaskGuide.Domain.Notifications;
using TaskGuide.Domain.Ranking;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.Domain.Time;

namespace TaskGuide.Application.Firing;

/// <summary>Pure, synchronous planning for the current day's Window fires.</summary>
public sealed class TickPlanner(
    IDayShapeReader shapes,
    DimensionRegistry registry,
    ClockTimeResolution resolution,
    DayBoundary boundary,
    StaleThresholds staleThresholds,
    Uri landingPage)
{
    private readonly IDayShapeReader _shapes = shapes;
    private readonly DimensionRegistry _registry = registry;
    private readonly ClockTimeResolution _resolution = resolution;
    private readonly DayBoundary _boundary = boundary;
    private readonly StaleThresholds _staleThresholds = staleThresholds;
    private readonly Uri _landingPage = landingPage;

    public TickPlan Plan(
        IStoreView view,
        DateTimeOffset now,
        IReadOnlyDictionary<DimensionId, IReadOnlyList<TagValue>> fetched,
        IReadOnlyList<DimensionId> failedFetches)
    {
        var date = _boundary.DateOf(now);
        var shape = _shapes.For(date);
        var fires = view.FiresOn(date).Rows;
        var dayFires = view.FiresOn(date);
        var carrier = Fallback.CarrierFor(view, date);
        var carried = Fallback.IsCarried(dayFires);
        var counter = new OpportunityCounter(_shapes, _registry, _resolution, _boundary);
        var events = Fallback.EventsForFooter(shape.Events, view, date);
        var footer = Footer(view, counter, now);
        var intents = new List<FireIntent>();

        foreach (var window in shape.Windows)
        {
            var resolved = _resolution.ResolveWindow(date, window);
            if (resolved is null || !FiringPolicy.IsWindowDue(window, resolved.Start, now)
                || !FiringPolicy.IsWindowAlive(window, resolved.End, now)
                || fires.Any(row => row.WindowId == window.Id && row.IsFired))
            {
                continue;
            }

            var duration = DurationCeiling(resolved, now);
            var matches = view.Tasks
                .Where(task => StatusRules.IsEligible(
                    task,
                    view.CompletionsFor(task.Id),
                    _registry,
                    _staleThresholds,
                    now,
                    _boundary))
                .Where(task => Matcher.Fits(
                    task,
                    new MatchContext(window, duration, fetched, failedFetches),
                    _registry))
                .ToList();

            var unconditional = carrier is not null && !carried;
            if (matches.Count == 0 && !unconditional) continue;

            var ranked = Rank(matches, counter, now, fetched, failedFetches);
            FireIntentKind kind = unconditional ? new UnconditionalFire() : new WindowFire(resolved);
            var dayBoundary = _boundary.EndOf(date);
            intents.Add(new FireIntent(
                kind,
                ranked,
                ranked.Count > 0
                    ? ReminderComposer.Compose(kind, ranked, events, footer, failedFetches, _landingPage,
                        TimeToLivePolicy.For(kind, dayBoundary))
                    : Fallback.Intent(
                        carrier ?? throw new InvalidOperationException("An unconditional fire requires a carrier Event."),
                        events,
                        footer,
                        failedFetches,
                        _landingPage,
                        dayBoundary).Reminder,
                new FireRow(
                    window.Id,
                    unconditional ? FireKind.Unconditional : FireKind.Window,
                    window.Name,
                    window.Start,
                    window.End,
                    DueAt: null,
                    FiredAt: null,
                    Matched: ranked.Count,
                    Carried: unconditional ? carrier?.Id : null)));

            carried |= unconditional;
        }

        if (carrier is { } fallbackCarrier
            && Fallback.IsDue(fallbackCarrier, shape.Windows, dayFires, now, _resolution, _boundary))
        {
            intents.Add(Fallback.Intent(fallbackCarrier, events, footer, failedFetches, _landingPage, _boundary.EndOf(date)));
        }

        return new TickPlan(intents.ToArray(), Glance(view, date, shape, now, fetched, failedFetches));
    }

    /// <summary>
    /// Weather is fetched only for an Active Task that actually constrains it. The tick owns this
    /// decision because an adapter cannot inspect the store-derived Status.
    /// </summary>
    public bool NeedsWeather(IStoreView view, DateTimeOffset now) =>
        view.Tasks.Any(task =>
            task.Tags.On(KnownDimensions.Weather).Count > 0
            && StatusRules.Of(task, view.CompletionsFor(task.Id), _registry, _staleThresholds, now, _boundary) is Status.Active);

    private GlanceState? Glance(
        IStoreView view,
        DateOnly date,
        DayShape shape,
        DateTimeOffset now,
        IReadOnlyDictionary<DimensionId, IReadOnlyList<TagValue>> fetched,
        IReadOnlyList<DimensionId> failedFetches)
    {
        var counter = new OpportunityCounter(_shapes, _registry, _resolution, _boundary);
        var count = view.Tasks.Count(task => StatusRules.Of(task, view.CompletionsFor(task.Id), _registry, _staleThresholds, now, _boundary) is not Status.Done);
        var live = shape.Windows
            .Select(window => _resolution.ResolveWindow(date, window))
            .FirstOrDefault(window => window is not null && FiringPolicy.IsWindowDue(window.Window, window.Start, now) && FiringPolicy.IsWindowAlive(window.Window, window.End, now));

        if (live is not null)
        {
            var matches = Matches(view, live, now, now, fetched, failedFetches);
            if (matches.Count > 0)
            {
                return new GlanceState(count, new InsideWindow(live, Rank(matches, counter, now, fetched, failedFetches), matches.Count));
            }
        }

        var next = NextWindow(date, now);
        if (next is null) return null;

        var nextMatches = Matches(view, next, next.Start, now, fetched, failedFetches);
        return new GlanceState(count, new NextWindow(next, Rank(nextMatches, counter, now, fetched, failedFetches)));
    }

    private ResolvedWindow? NextWindow(DateOnly date, DateTimeOffset now)
    {
        for (var offset = 0; offset < 8; offset++)
        {
            var candidateDate = date.AddDays(offset);
            var candidate = _shapes.For(candidateDate).Windows
                .Select(window => _resolution.ResolveWindow(candidateDate, window))
                .FirstOrDefault(window => window is not null && window.Start > now);
            if (candidate is not null) return candidate;
        }

        return null;
    }

    private List<TaskItem> Matches(
        IStoreView view,
        ResolvedWindow window,
        DateTimeOffset evaluationAt,
        DateTimeOffset statusAt,
        IReadOnlyDictionary<DimensionId, IReadOnlyList<TagValue>> fetched,
        IReadOnlyList<DimensionId> failedFetches) =>
        view.Tasks
            .Where(task => StatusRules.IsEligible(task, view.CompletionsFor(task.Id), _registry, _staleThresholds, statusAt, _boundary))
            .Where(task => Matcher.Fits(task, new MatchContext(window.Window, DurationCeiling(window, evaluationAt), fetched, failedFetches), _registry))
            .ToList();

    private IReadOnlyList<TaskItem> Rank(
        IReadOnlyList<TaskItem> matches,
        OpportunityCounter counter,
        DateTimeOffset now,
        IReadOnlyDictionary<DimensionId, IReadOnlyList<TagValue>> fetched,
        IReadOnlyList<DimensionId> failedFetches) =>
        Ranker.Rank(matches.Select(task => (
            task,
            Ranker.KeyFor(
                task,
                counter.CountAhead(task, now, fetched, failedFetches),
                _registry,
                now,
                _boundary))).ToList()).ToArray();

    private TagValue DurationCeiling(ResolvedWindow window, DateTimeOffset now)
    {
        var duration = _registry.Dimensions
            .Select(dimension => dimension.Value)
            .OfType<OrdinalDimension>()
            .Single(dimension => dimension.Id == KnownDimensions.Duration);
        return TaskGuide.Domain.Matching.DurationCeiling.WindowCeiling(window.End - now, duration.OrderedValues);
    }

    private FooterCounts Footer(IStoreView view, OpportunityCounter counter, DateTimeOffset now)
    {
        var statuses = view.Tasks.Select(task => (task, status: StatusRules.Of(
            task,
            view.CompletionsFor(task.Id),
            _registry,
            _staleThresholds,
            now,
            _boundary))).ToArray();
        var weekOf = _boundary.DateOf(now).AddDays(-(int)_boundary.DateOf(now).DayOfWeek);

        return new FooterCounts(
            statuses.Count(entry => entry.status == Status.Unprocessed),
            statuses.Count(entry => entry.status == Status.Stale),
            statuses.Count(entry => entry.status == Status.Active
                && OrphanDetection.IsTaskOrphan(
                    entry.status,
                    counter.CountInPatternWeek(entry.task, view.Patterns.Active, view.DayTemplates, weekOf))));
    }
}
