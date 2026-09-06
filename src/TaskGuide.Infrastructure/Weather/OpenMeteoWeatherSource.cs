using System.Text.Json;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Time;

namespace TaskGuide.Infrastructure.Weather;

/// <summary>
/// Open-Meteo-backed weather for Task Guide's single household. The home coordinate is a rule,
/// not deployment configuration (#86): the service has one location, and location changes are a
/// code change like the weather vocabulary.
/// </summary>
/// <remarks>
/// One Open-Meteo response holds current, hourly and daily weather. It is deliberately one
/// payload with two readers: current conditions refresh after the provider's 15-minute interval,
/// while forecast point queries tolerate the settled three-hour forecast lifetime. The port stays
/// point-shaped so callers never learn about either grid or cache policy.
/// </remarks>
public sealed class OpenMeteoWeatherSource(IHttpClientFactory httpClientFactory, TimeProvider clock) : IWeatherSource
{
    public const string HttpClientName = "open-meteo";

    private const string Latitude = "39.9612";
    private const string Longitude = "-82.9988";
    private static readonly string ForecastUrl = "https://api.open-meteo.com/v1/forecast"
        + $"?latitude={Latitude}&longitude={Longitude}&timezone=America%2FChicago&forecast_days=7"
        + "&current=weather_code,precipitation"
        + "&hourly=weather_code,precipitation"
        + "&daily=weather_code,precipitation_sum";
    private static readonly TimeSpan CurrentTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ForecastTtl = TimeSpan.FromHours(3);
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(DayBoundary.ZoneId);

    private readonly SemaphoreSlim _fetchGate = new(1, 1);
    private CachedPayload? _cached;

    public async Task<FetchOutcome<IReadOnlyList<TagValue>>> CurrentAsync(CancellationToken cancellationToken)
    {
        var payload = await PayloadAsync(CurrentTtl, cancellationToken);
        return payload.Match<FetchOutcome<IReadOnlyList<TagValue>>>(
            cached => new Known<IReadOnlyList<TagValue>>([cached.Value.Payload.Current]),
            unavailable => new Unavailable(unavailable.Reason));
    }

    public async Task<FetchOutcome<IReadOnlyList<TagValue>>> ForecastAsync(
        DateOnly date,
        TimeOnly at,
        CancellationToken cancellationToken)
    {
        var payload = await PayloadAsync(ForecastTtl, cancellationToken);
        return payload.Match<FetchOutcome<IReadOnlyList<TagValue>>>(
            cached => ForecastAt(cached.Value.Payload, date, at),
            unavailable => new Unavailable(unavailable.Reason));
    }

    private async Task<FetchOutcome<CachedPayload>> PayloadAsync(TimeSpan ttl, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        if (_cached is { } cached && now - cached.FetchedAt < ttl)
        {
            return new Known<CachedPayload>(cached);
        }

        await _fetchGate.WaitAsync(cancellationToken);
        try
        {
            now = clock.GetUtcNow();
            if (_cached is { } refreshed && now - refreshed.FetchedAt < ttl)
            {
                return new Known<CachedPayload>(refreshed);
            }

            try
            {
                var httpClient = httpClientFactory.CreateClient(HttpClientName);
                using var response = await httpClient.GetAsync(ForecastUrl, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return new Unavailable($"Open-Meteo returned HTTP {(int)response.StatusCode}");
                }

                await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
                var parsed = Parse(document.RootElement);
                _cached = new CachedPayload(now, parsed);
                return new Known<CachedPayload>(_cached);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException or FormatException or OperationCanceledException)
            {
                return new Unavailable($"Open-Meteo response unavailable: {exception.Message}");
            }
        }
        finally
        {
            _fetchGate.Release();
        }
    }

    private FetchOutcome<IReadOnlyList<TagValue>> ForecastAt(WeatherPayload payload, DateOnly date, TimeOnly at)
    {
        // The next 48 hours use the hourly grid. Further out, a daily row is the intentionally
        // coarser forecast the ticket calls for; callers still observe one per-point port.
        var point = date.ToDateTime(at);
        var now = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Zone).DateTime;
        var nearFront = point - now < TimeSpan.FromHours(48);
        if (nearFront)
        {
            // Availability Windows can start between hourly boundaries. The provider's hourly row
            // is an hour bucket, so 10:30 reads 10:00 rather than falling through to a daily
            // aggregate and losing near-horizon granularity.
            var hourlyPoint = new DateTime(point.Year, point.Month, point.Day, point.Hour, 0, 0);
            return payload.Hourly.TryGetValue(hourlyPoint, out var hourly)
                ? new Known<IReadOnlyList<TagValue>>([hourly])
                : new Unavailable($"Open-Meteo has no hourly forecast for {point:yyyy-MM-dd HH:mm}");
        }

        return payload.Daily.TryGetValue(date, out var daily)
            ? new Known<IReadOnlyList<TagValue>>([daily])
            : new Unavailable($"Open-Meteo has no forecast for {date:yyyy-MM-dd}");
    }

    private static WeatherPayload Parse(JsonElement root)
    {
        var current = ReadBucket(root.GetProperty("current").GetProperty("weather_code"), root.GetProperty("current").GetProperty("precipitation"));
        return new WeatherPayload(
            current,
            ReadHourly(root.GetProperty("hourly")),
            ReadDaily(root.GetProperty("daily")));
    }

    private static IReadOnlyDictionary<DateTime, TagValue> ReadHourly(JsonElement hourly)
    {
        var times = hourly.GetProperty("time").EnumerateArray().ToArray();
        var codes = hourly.GetProperty("weather_code").EnumerateArray().ToArray();
        var precipitation = hourly.GetProperty("precipitation").EnumerateArray().ToArray();
        if (times.Length != codes.Length || times.Length != precipitation.Length)
        {
            throw new InvalidOperationException("Open-Meteo hourly arrays have different lengths");
        }

        return Enumerable.Range(0, times.Length).ToDictionary(
            index => DateTime.Parse(times[index].GetString()!),
            index => ReadBucket(codes[index], precipitation[index]));
    }

    private static IReadOnlyDictionary<DateOnly, TagValue> ReadDaily(JsonElement daily)
    {
        var dates = daily.GetProperty("time").EnumerateArray().ToArray();
        var codes = daily.GetProperty("weather_code").EnumerateArray().ToArray();
        var precipitation = daily.GetProperty("precipitation_sum").EnumerateArray().ToArray();
        if (dates.Length != codes.Length || dates.Length != precipitation.Length)
        {
            throw new InvalidOperationException("Open-Meteo daily arrays have different lengths");
        }

        return Enumerable.Range(0, dates.Length).ToDictionary(
            index => DateOnly.Parse(dates[index].GetString()!),
            index => ReadBucket(codes[index], precipitation[index]));
    }

    private static TagValue ReadBucket(JsonElement weatherCode, JsonElement precipitation)
    {
        var code = weatherCode.GetInt32();
        var amount = precipitation.GetDecimal();

        // This implementation chooses a 0.0 mm threshold: any measured precipitation is wet, while an overcast
        // (WMO 3) observation with 0.0 mm is dry. This is a fixed domain rule, not configuration.
        return code switch
        {
            >= 71 and <= 77 or >= 85 and <= 86 => new TagValue("snow"),
            >= 51 and <= 67 or >= 80 and <= 82 or >= 95 and <= 99 => new TagValue("wet"),
            >= 0 and <= 3 or >= 45 and <= 48 when amount > 0 => new TagValue("wet"),
            >= 0 and <= 3 or >= 45 and <= 48 => new TagValue("dry"),
            _ => throw new InvalidOperationException($"Open-Meteo returned unsupported WMO code {code}"),
        };
    }

    private sealed record CachedPayload(DateTimeOffset FetchedAt, WeatherPayload Payload);

    private sealed record WeatherPayload(
        TagValue Current,
        IReadOnlyDictionary<DateTime, TagValue> Hourly,
        IReadOnlyDictionary<DateOnly, TagValue> Daily);
}
