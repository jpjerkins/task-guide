using System.Net;
using System.Text;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Tags;
using TaskGuide.Infrastructure.Weather;
using Xunit;

namespace TaskGuide.Infrastructure.Tests;

/// <summary>
/// `tests/TEST-INVENTORY.md`, "Weather, the fetched axis": the adapter maps Open-Meteo's
/// one seven-day response into the port's point-shaped queries without exposing cache or grid
/// mechanics to the caller.
/// </summary>
public sealed class OpenMeteoWeatherSourceTests
{
    private sealed class CapturingHandler(params Func<HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var index = Math.Min(Requests.Count - 1, responses.Length - 1);
            return Task.FromResult(responses[index]());
        }
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task A_firing_uses_current_conditions_and_a_future_evaluation_uses_the_forecast()
    {
        var handler = new CapturingHandler(Response(currentCode: 3, currentPrecipitation: 0, hourlyCode: 61, hourlyPrecipitation: 1));
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 6, 14, 0, 0, TimeSpan.Zero));
        var weather = CreateSource(handler, clock);

        var current = await weather.CurrentAsync(CancellationToken.None);
        var forecast = await weather.ForecastAsync(new DateOnly(2026, 9, 6), new TimeOnly(10, 30), CancellationToken.None);

        Assert.True(current.IsT0);
        Assert.True(forecast.IsT0);
        Assert.Equal(new TagValue("dry"), current.AsT0.Value.Single());
        Assert.Equal(new TagValue("wet"), forecast.AsT0.Value.Single());
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Current_conditions_are_memoized_for_one_tick_interval()
    {
        var handler = new CapturingHandler(Response(currentCode: 3, currentPrecipitation: 0), Response(currentCode: 71, currentPrecipitation: 1));
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 6, 14, 0, 0, TimeSpan.Zero));
        var weather = CreateSource(handler, clock);

        await weather.CurrentAsync(CancellationToken.None);
        clock.Now += TimeSpan.FromMinutes(14);
        await weather.CurrentAsync(CancellationToken.None);
        clock.Now += TimeSpan.FromMinutes(1);
        var refreshed = await weather.CurrentAsync(CancellationToken.None);

        Assert.True(refreshed.IsT0);
        Assert.Equal(new TagValue("snow"), refreshed.AsT0.Value.Single());
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task A_bulk_forecast_fetch_serves_point_queries_until_its_TTL_expires()
    {
        var handler = new CapturingHandler(Response(hourlyCode: 61, hourlyPrecipitation: 1));
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 6, 14, 0, 0, TimeSpan.Zero));
        var weather = CreateSource(handler, clock);

        await weather.ForecastAsync(new DateOnly(2026, 9, 6), new TimeOnly(10, 0), CancellationToken.None);
        await weather.ForecastAsync(new DateOnly(2026, 9, 7), new TimeOnly(12, 0), CancellationToken.None);
        clock.Now += TimeSpan.FromHours(3);
        await weather.ForecastAsync(new DateOnly(2026, 9, 7), new TimeOnly(12, 0), CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task A_distant_forecast_point_uses_daily_resolution()
    {
        var handler = new CapturingHandler(Response(hourlyCode: 61, hourlyPrecipitation: 1, dailyCode: 71, dailyPrecipitation: 1));
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 6, 14, 0, 0, TimeSpan.Zero));
        var weather = CreateSource(handler, clock);

        var forecast = await weather.ForecastAsync(new DateOnly(2026, 9, 9), new TimeOnly(12, 0), CancellationToken.None);

        Assert.True(forecast.IsT0);
        Assert.Equal(new TagValue("snow"), forecast.AsT0.Value.Single());
    }

    [Fact]
    public async Task An_unavailable_weather_fetch_is_never_represented_as_an_empty_value_set()
    {
        var handler = new CapturingHandler(() => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 6, 14, 0, 0, TimeSpan.Zero));
        var weather = CreateSource(handler, clock);

        var outcome = await weather.CurrentAsync(CancellationToken.None);

        Assert.True(outcome.IsT1);
    }

    [Fact]
    public async Task A_timed_out_weather_fetch_is_unavailable()
    {
        var handler = new CapturingHandler(() => throw new TaskCanceledException("Open-Meteo timed out"));
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 6, 14, 0, 0, TimeSpan.Zero));
        var weather = CreateSource(handler, clock);

        var outcome = await weather.CurrentAsync(CancellationToken.None);

        Assert.True(outcome.IsT1);
    }

    private static IWeatherSource CreateSource(CapturingHandler handler, TimeProvider clock)
    {
        return new OpenMeteoWeatherSource(new StubHttpClientFactory(new HttpClient(handler)), clock);
    }

    private static Func<HttpResponseMessage> Response(
        int currentCode = 3,
        decimal currentPrecipitation = 0,
        int hourlyCode = 3,
        decimal hourlyPrecipitation = 0,
        int dailyCode = 3,
        decimal dailyPrecipitation = 0) =>
        () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""
                {
                  "current": { "weather_code": {{currentCode}}, "precipitation": {{currentPrecipitation}} },
                  "hourly": {
                    "time": ["2026-09-06T10:00", "2026-09-07T12:00"],
                    "weather_code": [{{hourlyCode}}, {{hourlyCode}}],
                    "precipitation": [{{hourlyPrecipitation}}, {{hourlyPrecipitation}}]
                  },
                  "daily": {
                    "time": ["2026-09-06", "2026-09-07", "2026-09-09"],
                    "weather_code": [{{dailyCode}}, {{dailyCode}}, {{dailyCode}}],
                    "precipitation_sum": [{{dailyPrecipitation}}, {{dailyPrecipitation}}, {{dailyPrecipitation}}]
                  }
                }
                """,
                Encoding.UTF8,
                "application/json"),
        };
}
