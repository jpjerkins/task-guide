using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OneOf;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using Xunit;

namespace TaskGuide.Api.Tests;

public sealed class ReminderEndpointsTests : IDisposable
{
    private const string DataDirEnvVar = "Storage__DataDir";
    private readonly string _dataDir = Directory.CreateTempSubdirectory("taskguide-reminder-api-tests-").FullName;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ReminderEndpointsTests()
    {
        var fires = Path.Combine(_dataDir, "fires");
        Directory.CreateDirectory(fires);
        File.WriteAllText(Path.Combine(fires, "2026-09-05.json"), """
            [{ "windowId": "w_late", "kind": "window", "windowName": "Late",
               "windowStart": "23:45", "windowEnd": "23:55", "dueAt": null,
               "firedAt": "2026-09-06T04:45:00Z", "matched": 1, "carried": null }]
            """);

        (_factory, _client) = BuildFactory(_dataDir, new DateTimeOffset(2026, 9, 6, 4, 56, 0, TimeSpan.Zero));
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        Directory.Delete(_dataDir, recursive: true);
    }

    [Fact]
    public async Task POST_api_reminders_date_windowId_snooze_rejects_a_re_fire_crossing_the_day_boundary_with_the_same_line_the_disabled_control_shows()
    {
        var response = await _client.PostAsync("/api/reminders/2026-09-05/w_late/snooze", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("Snooze ends at midnight", body?.Error);
    }

    [Fact]
    public async Task GET_api_reminders_date_windowId_returns_the_Windows_own_name_span_and_date_and_all_matches_rather_than_the_pushs_three()
    {
        await WriteAsync(_factory, new OverridesWrite([new DateOverride(new DateOnly(2026, 9, 5), [Window("w_late", 23, 45, 23, 55)], null)]));
        await WriteAsync(_factory, new TasksWrite(FourTasksOfDuration("2")));

        var doc = await GetAsync("/api/reminders/2026-09-05/w_late");

        Assert.Equal("2026-09-05", doc.GetProperty("date").GetString());
        Assert.Equal("Late", doc.GetProperty("windowName").GetString());
        Assert.Equal(new TimeOnly(23, 45), TimeOnly.Parse(doc.GetProperty("windowStart").GetString()!));
        Assert.Equal(new TimeOnly(23, 55), TimeOnly.Parse(doc.GetProperty("windowEnd").GetString()!));
        Assert.Equal(4, doc.GetProperty("matches").GetArrayLength());
    }

    [Fact]
    public async Task GET_api_reminders_date_windowId_re_derives_Durations_ceiling_from_the_time_actually_remaining_so_a_page_opened_late_offers_no_Task_longer_than_the_span_left()
    {
        await WriteAsync(_factory, new OverridesWrite([new DateOverride(new DateOnly(2026, 9, 5), [Window("w_late", 23, 45, 23, 55)], null)]));
        await WriteAsync(_factory, new TasksWrite([NewTask("t_two", "2"), NewTask("t_ten", "10")]));

        var doc = await GetAsync("/api/reminders/2026-09-05/w_late");

        var matches = doc.GetProperty("matches").EnumerateArray().ToArray();
        Assert.Single(matches);
        Assert.Equal("t_two", matches[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task GET_api_reminders_date_windowId_carries_the_Snooze_interval_the_server_computed_and_its_suppression_line_is_the_one_the_rejected_POST_returns()
    {
        var doc = await GetAsync("/api/reminders/2026-09-05/w_late");

        var snooze = doc.GetProperty("snooze");
        Assert.Equal(5, snooze.GetProperty("intervalMinutes").GetInt32());
        Assert.Equal("Snooze ends at midnight", snooze.GetProperty("suppression").GetString());
        Assert.Equal("window", doc.GetProperty("firedAs").GetString());
    }

    [Fact]
    public async Task GET_api_reminders_date_windowId_past_the_Reminders_Day_boundary_reports_the_page_not_live_with_This_reminder_was_for_yesterday()
    {
        var (lateFactory, lateClient) = BuildFactory(_dataDir, new DateTimeOffset(2026, 9, 6, 5, 5, 0, TimeSpan.Zero));
        using var disposableLateFactory = lateFactory;
        using var disposableLateClient = lateClient;

        var response = await lateClient.GetAsync("/api/reminders/2026-09-05/w_late");
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(doc.GetProperty("isLive").GetBoolean());
        Assert.Equal("This reminder was for yesterday", doc.GetProperty("staleLine").GetString());
    }

    [Fact]
    public async Task GET_api_reminders_date_fallback_carries_no_Snooze_at_all()
    {
        var carrier = new Event(new EventId("evt_trip"), new DateOnly(2026, 9, 7), "Family trip", new TimeOnly(9, 0), new TimeOnly(10, 0), TagSet.Empty, null);
        await WriteAsync(_factory, new EventsWrite([carrier]));
        var fired = new FireRow(null, FireKind.Fallback, null, null, null, null,
            new DateTimeOffset(2026, 9, 7, 16, 0, 0, TimeSpan.Zero), null, carrier.Id);
        await WriteAsync(_factory, new FiresWrite(new DayFires(new DateOnly(2026, 9, 7), [fired])));

        var doc = await GetAsync("/api/reminders/2026-09-07/fallback");

        Assert.Equal(JsonValueKind.Null, doc.GetProperty("snooze").ValueKind);
        Assert.Equal("Family trip", doc.GetProperty("fallbackEventName").GetString());
        Assert.Equal("fallback", doc.GetProperty("firedAs").GetString());
    }

    [Fact]
    public async Task GET_api_reminders_date_windowId_names_which_fire_actually_happened_so_an_empty_page_tells_the_silent_case_apart_from_an_unconditional_fire_an_empty_Snooze_re_fire_and_a_fallback_push()
    {
        await WriteAsync(_factory, new OverridesWrite([new DateOverride(new DateOnly(2026, 9, 8), [Window("w_silent", 9, 0, 10, 0)], null)]));
        var silent = await GetAsync("/api/reminders/2026-09-08/w_silent");
        Assert.Equal(JsonValueKind.Null, silent.GetProperty("firedAs").ValueKind);

        await WriteAsync(_factory, new OverridesWrite([new DateOverride(new DateOnly(2026, 9, 9), [Window("w_uncond", 9, 0, 10, 0)], null)]));
        var unconditional = new FireRow(new WindowId("w_uncond"), FireKind.Unconditional, "Morning", new TimeOnly(9, 0), new TimeOnly(10, 0),
            null, new DateTimeOffset(2026, 9, 9, 14, 0, 0, TimeSpan.Zero), 0, null);
        await WriteAsync(_factory, new FiresWrite(new DayFires(new DateOnly(2026, 9, 9), [unconditional])));
        var unconditionalPage = await GetAsync("/api/reminders/2026-09-09/w_uncond");
        Assert.Equal("unconditional", unconditionalPage.GetProperty("firedAs").GetString());
    }

    [Fact]
    public async Task GET_api_reminders_date_windowId_carries_the_footer_counts_and_names_a_failed_fetched_Dimension_check_by_its_Dimension_id()
    {
        var (weatherFactory, weatherClient) = BuildFactory(_dataDir, new DateTimeOffset(2026, 9, 6, 4, 56, 0, TimeSpan.Zero), new AlwaysUnavailableWeatherSource());
        using var disposableWeatherFactory = weatherFactory;
        using var disposableWeatherClient = weatherClient;
        await WriteAsync(weatherFactory, new OverridesWrite([new DateOverride(new DateOnly(2026, 9, 10), [Window("w_weather", 9, 0, 10, 0)], null)]));
        await WriteAsync(weatherFactory, new TasksWrite([NewTask("t_weather", "10", weather: "dry")]));

        var response = await weatherClient.GetAsync("/api/reminders/2026-09-10/w_weather");
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();

        var failed = doc.GetProperty("failedFetches").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(["weather"], failed);
        var footer = doc.GetProperty("footer");
        Assert.True(footer.TryGetProperty("toProcess", out _));
        Assert.True(footer.TryGetProperty("stale", out _));
        Assert.True(footer.TryGetProperty("orphans", out _));
    }

    [Fact]
    public async Task GET_api_reminders_date_windowId_splits_Matching_on_into_the_axes_the_Window_declares_and_the_axes_left_to_the_window_side_default()
    {
        var window = new AvailabilityWindow(new WindowId("w_home"), "Home", new TimeOnly(9, 0), new TimeOnly(10, 0),
            new TagSet(new Dictionary<DimensionId, IReadOnlyList<TagValue>> { [KnownDimensions.Location] = [new TagValue("home")] }, []));
        await WriteAsync(_factory, new OverridesWrite([new DateOverride(new DateOnly(2026, 9, 11), [window], null)]));

        var doc = await GetAsync("/api/reminders/2026-09-11/w_home");

        var matchingOn = doc.GetProperty("matchingOn");
        Assert.Equal(["home"], matchingOn.GetProperty("declared").GetProperty("location").EnumerateArray().Select(e => e.GetString()!).ToArray());
        Assert.False(matchingOn.GetProperty("defaulted").TryGetProperty("location", out _));
        Assert.Equal(["longer"], matchingOn.GetProperty("defaulted").GetProperty("duration").EnumerateArray().Select(e => e.GetString()!).ToArray());
        Assert.Equal(["low"], matchingOn.GetProperty("defaulted").GetProperty("energy").EnumerateArray().Select(e => e.GetString()!).ToArray());
        Assert.Empty(matchingOn.GetProperty("defaulted").GetProperty("withWhom").EnumerateArray());
    }

    [Fact]
    public async Task GET_api_reminders_date_windowId_is_404_when_neither_a_fire_row_nor_the_days_shape_knows_that_Window_and_400_for_an_unparseable_date()
    {
        var notFound = await _client.GetAsync("/api/reminders/2026-09-05/nope");
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);

        var badDate = await _client.GetAsync("/api/reminders/not-a-date/w_late");
        Assert.Equal(HttpStatusCode.BadRequest, badDate.StatusCode);
        var body = await badDate.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("date must be an ISO date", body?.Error);
    }

    [Fact]
    public async Task GET_api_reminders_date_windowId_200_shape_is_typed_in_OpenAPI_for_SPA_generation()
    {
        var response = await _client.GetAsync("/openapi/v1.json");
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();

        var schemaRef = doc.GetProperty("paths").GetProperty("/api/reminders/{date}/{windowId}").GetProperty("get")
            .GetProperty("responses").GetProperty("200").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString();

        Assert.Equal("#/components/schemas/ReminderPageResponse", schemaRef);
    }

    private async Task<JsonElement> GetAsync(string url)
    {
        var response = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task WriteAsync(WebApplicationFactory<Program> factory, params object[] writes)
    {
        var store = factory.Services.GetRequiredService<IStore>();
        await store.MutateAsync<Never>(_ => OneOf<StoreMutation, Never>.FromT0(new StoreMutation(writes)), CancellationToken.None);
    }

    private static (WebApplicationFactory<Program> Factory, HttpClient Client) BuildFactory(
        string dataDir, DateTimeOffset now, TaskGuide.Application.Ports.IWeatherSource? weather = null)
    {
        Environment.SetEnvironmentVariable(DataDirEnvVar, dataDir);
        try
        {
            var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));
                    if (weather is not null)
                    {
                        services.RemoveAll<TaskGuide.Application.Ports.IWeatherSource>();
                        services.AddSingleton(weather);
                    }
                }));
            return (factory, factory.CreateClient()); // forces host startup now, while the env var is still set
        }
        finally
        {
            Environment.SetEnvironmentVariable(DataDirEnvVar, null);
        }
    }

    private static AvailabilityWindow Window(string id, int startHour, int startMinute, int endHour, int endMinute) =>
        new(new WindowId(id), "Late", new TimeOnly(startHour, startMinute), new TimeOnly(endHour, endMinute), TagSet.Empty);

    private static TaskItem[] FourTasksOfDuration(string duration) =>
        [NewTask("t1", duration), NewTask("t2", duration), NewTask("t3", duration), NewTask("t4", duration)];

    private static TaskItem NewTask(string id, string duration, string? weather = null) => new(
        new TaskId(id), id, null,
        new TagSet(new Dictionary<DimensionId, IReadOnlyList<TagValue>>
        {
            [KnownDimensions.Duration] = [new TagValue(duration)],
            [KnownDimensions.Weather] = weather is null ? [] : [new TagValue(weather)],
        }, []),
        null, null, null, null, DateTimeOffset.UtcNow.AddDays(-1));

    private sealed record ErrorResponse(string Error);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class AlwaysUnavailableWeatherSource : TaskGuide.Application.Ports.IWeatherSource
    {
        public Task<FetchOutcome<IReadOnlyList<TagValue>>> CurrentAsync(CancellationToken cancellationToken) =>
            Task.FromResult<FetchOutcome<IReadOnlyList<TagValue>>>(new Unavailable("test"));

        public Task<FetchOutcome<IReadOnlyList<TagValue>>> ForecastAsync(DateOnly date, TimeOnly at, CancellationToken cancellationToken) =>
            Task.FromResult<FetchOutcome<IReadOnlyList<TagValue>>>(new Unavailable("test"));
    }
}
