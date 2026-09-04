using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Tags;

namespace TaskGuide.TestSupport;

/// <summary>
/// Records every call and returns a configurable <see cref="FetchOutcome{T}"/>, defaulting both
/// axes to <see cref="Unavailable"/> — matching fails closed (#68), so an unconfigured fake must
/// not silently look like fine weather.
/// </summary>
public sealed class FakeWeatherSource : IWeatherSource
{
    private FetchOutcome<IReadOnlyList<TagValue>> _current = new Unavailable("not configured");
    private FetchOutcome<IReadOnlyList<TagValue>> _forecast = new Unavailable("not configured");

    public int CurrentCallCount { get; private set; }
    public List<(DateOnly Date, TimeOnly At)> ForecastCalls { get; } = [];

    public void SetCurrent(FetchOutcome<IReadOnlyList<TagValue>> outcome) => _current = outcome;
    public void SetForecast(FetchOutcome<IReadOnlyList<TagValue>> outcome) => _forecast = outcome;

    public Task<FetchOutcome<IReadOnlyList<TagValue>>> CurrentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CurrentCallCount++;
        return Task.FromResult(_current);
    }

    public Task<FetchOutcome<IReadOnlyList<TagValue>>> ForecastAsync(DateOnly date, TimeOnly at, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ForecastCalls.Add((date, at));
        return Task.FromResult(_forecast);
    }
}
