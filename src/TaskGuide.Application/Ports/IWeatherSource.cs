using TaskGuide.Domain.Tags;

namespace TaskGuide.Application.Ports;

/// <summary>
/// The one fetched Dimension source. <b>Lazy</b>: the check only runs if some Active Task
/// actually carries a Weather Tag — no weather-tagged Tasks, no API call.
/// </summary>
public interface IWeatherSource
{
    /// <summary>Current conditions, for deciding whether a Window fires right now.</summary>
    Task<IReadOnlyList<TagValue>?> CurrentAsync(CancellationToken cancellationToken);

    /// <summary>Forecast, for evaluating a future Window (ranking, Scarcity).</summary>
    Task<IReadOnlyList<TagValue>?> ForecastAsync(DateOnly date, TimeOnly at, CancellationToken cancellationToken);
}
