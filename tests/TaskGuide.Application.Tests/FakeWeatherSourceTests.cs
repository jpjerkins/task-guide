using TaskGuide.Domain.Common;
using TaskGuide.Domain.Tags;
using TaskGuide.TestSupport;
using Xunit;

namespace TaskGuide.Application.Tests;

/// <summary>
/// `tests/TEST-INVENTORY.md`, "Test support (#77)": <see cref="TaskGuide.TestSupport.FakeWeatherSource"/>
/// defaults both axes to <see cref="Unavailable"/> — matching fails closed (#68), so an
/// unconfigured fake must not silently look like fine weather.
/// </summary>
public sealed class FakeWeatherSourceTests
{
    [Fact]
    public async Task An_unconfigured_FakeWeatherSource_is_Unavailable_on_both_axes()
    {
        var weather = new FakeWeatherSource();

        var current = await weather.CurrentAsync(CancellationToken.None);
        var forecast = await weather.ForecastAsync(new DateOnly(2026, 9, 5), new TimeOnly(9, 0), CancellationToken.None);

        Assert.True(current.IsT1);
        Assert.True(forecast.IsT1);
    }

    [Fact]
    public async Task A_configured_FakeWeatherSource_yields_its_known_value_and_records_the_call()
    {
        var weather = new FakeWeatherSource();
        var values = (IReadOnlyList<TagValue>)[new TagValue("clear")];
        weather.SetCurrent(new Known<IReadOnlyList<TagValue>>(values));
        weather.SetForecast(new Known<IReadOnlyList<TagValue>>(values));

        var current = await weather.CurrentAsync(CancellationToken.None);
        var forecast = await weather.ForecastAsync(new DateOnly(2026, 9, 5), new TimeOnly(9, 0), CancellationToken.None);

        Assert.True(current.IsT0);
        Assert.Equal(values, current.AsT0.Value);
        Assert.True(forecast.IsT0);
        Assert.Equal(1, weather.CurrentCallCount);
        Assert.Equal((new DateOnly(2026, 9, 5), new TimeOnly(9, 0)), Assert.Single(weather.ForecastCalls));
    }

    [Fact]
    public async Task CurrentAsync_throws_for_an_already_cancelled_token()
    {
        var weather = new FakeWeatherSource();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => weather.CurrentAsync(cts.Token));
    }

    [Fact]
    public async Task ForecastAsync_throws_for_an_already_cancelled_token()
    {
        var weather = new FakeWeatherSource();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => weather.ForecastAsync(new DateOnly(2026, 9, 5), new TimeOnly(9, 0), cts.Token));
    }
}
