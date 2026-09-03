using TaskGuide.Domain.Common;
using TaskGuide.Domain.Tags;

namespace TaskGuide.Application.Ports;

/// <summary>
/// The one fetched Dimension source. <b>Lazy</b>: the check only runs if some Active Task
/// actually carries a Weather Tag — no weather-tagged Tasks, no API call.
/// </summary>
/// <remarks>
/// Returns <see cref="FetchOutcome{T}"/>, not a nullable list (#69/#76): #68 makes the
/// unknown/empty distinction load-bearing in <b>opposite</b> directions — matching fails closed,
/// counting fails loud — and a single <c>?? []</c> would collapse that silently.
/// </remarks>
public interface IWeatherSource
{
    /// <summary>Current conditions, for deciding whether a Window fires right now.</summary>
    Task<FetchOutcome<IReadOnlyList<TagValue>>> CurrentAsync(CancellationToken cancellationToken);

    /// <summary>Forecast, for evaluating a future Window (ranking, Scarcity).</summary>
    Task<FetchOutcome<IReadOnlyList<TagValue>>> ForecastAsync(DateOnly date, TimeOnly at, CancellationToken cancellationToken);
}
