using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Tags;

namespace TaskGuide.Application.Firing;

/// <summary>Drives one firing pass by planning from the current store view, then executing it.</summary>
public sealed class TickService(IStore store, TickPlanner planner, TickExecutor executor, IWeatherSource weather) : ITickLoop
{
    private readonly IStore _store = store;
    private readonly TickPlanner _planner = planner;
    private readonly TickExecutor _executor = executor;
    private readonly IWeatherSource _weather = weather;

    public async Task TickAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var view = _store.Read();
        var (fetched, failedFetches) = await WeatherForAsync(view, now, cancellationToken);
        var plan = _planner.Plan(
            view,
            now,
            fetched,
            failedFetches);
        await _executor.ExecuteAsync(plan, now, cancellationToken);
    }

    private async Task<(IReadOnlyDictionary<DimensionId, IReadOnlyList<TagValue>> Fetched, IReadOnlyList<DimensionId> Failed)> WeatherForAsync(
        IStoreView view,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!_planner.NeedsWeather(view, now))
        {
            return (new Dictionary<DimensionId, IReadOnlyList<TagValue>>(), []);
        }

        var outcome = await _weather.CurrentAsync(cancellationToken);
        if (outcome.Value is Known<IReadOnlyList<TagValue>> known)
        {
            return (new Dictionary<DimensionId, IReadOnlyList<TagValue>> { [KnownDimensions.Weather] = known.Value }, []);
        }

        if (outcome.Value is Unavailable)
        {
            return (new Dictionary<DimensionId, IReadOnlyList<TagValue>>(), [KnownDimensions.Weather]);
        }

        throw new InvalidOperationException("Every fetch outcome must be known or unavailable.");
    }
}
