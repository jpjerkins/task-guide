using TaskGuide.Application.Ports;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Tags;

namespace TaskGuide.Application.Firing;

/// <summary>Drives one firing pass by planning from the current store view, then executing it.</summary>
public sealed class TickService(IStore store, TickPlanner planner, TickExecutor executor) : ITickLoop
{
    private readonly IStore _store = store;
    private readonly TickPlanner _planner = planner;
    private readonly TickExecutor _executor = executor;

    public Task TickAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var plan = _planner.Plan(
            _store.Read(),
            now,
            new Dictionary<DimensionId, IReadOnlyList<TagValue>>(),
            []);
        return _executor.ExecuteAsync(plan, now, cancellationToken);
    }
}
