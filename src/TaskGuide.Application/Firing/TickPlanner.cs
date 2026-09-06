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
    StaleThresholds staleThresholds)
{
    private readonly IDayShapeReader _shapes = shapes;
    private readonly DimensionRegistry _registry = registry;
    private readonly ClockTimeResolution _resolution = resolution;
    private readonly DayBoundary _boundary = boundary;
    private readonly StaleThresholds _staleThresholds = staleThresholds;

    public TickPlan Plan(
        IStoreView view,
        DateTimeOffset now,
        IReadOnlyDictionary<DimensionId, IReadOnlyList<TagValue>> fetched,
        IReadOnlyList<DimensionId> failedFetches)
    {
        var date = _boundary.DateOf(now);
        var shape = _shapes.For(date);
        var fires = view.FiresOn(date).Rows;
        var counter = new OpportunityCounter(_shapes, _registry, _resolution, _boundary);
        var intents = new List<FireIntent>();

        foreach (var window in shape.Windows)
        {
            var resolved = _resolution.ResolveWindow(date, window);
            if (resolved is null || !FiringPolicy.IsWindowDue(window, resolved.Start, now)
                || !FiringPolicy.IsWindowAlive(window, resolved.End, now)
                || fires.Any(row => row.WindowId == window.Id && row.Kind == FireKind.Window && row.IsFired))
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

            if (matches.Count == 0) continue;

            var ranked = Rank(matches, counter, now, fetched, failedFetches);
            intents.Add(new FireIntent(
                new WindowFire(resolved),
                ranked,
                resolved.End,
                new FireRow(
                    window.Id,
                    FireKind.Window,
                    window.Name,
                    window.Start,
                    window.End,
                    DueAt: null,
                    FiredAt: null,
                    Matched: ranked.Count,
                    Carried: null)));
        }

        return new TickPlan(intents.ToArray(), Glance: null);
    }

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
}
