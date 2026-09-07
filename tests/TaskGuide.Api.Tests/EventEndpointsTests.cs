using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OneOf;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using Xunit;

namespace TaskGuide.Api.Tests;

public sealed class EventEndpointsTests : IDisposable
{
    private const string DataDirEnvVar = "Storage__DataDir";
    private readonly string _dataDir = Directory.CreateTempSubdirectory("taskguide-api-tests-").FullName;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public EventEndpointsTests()
    {
        Environment.SetEnvironmentVariable(DataDirEnvVar, _dataDir);
        try
        {
            _factory = new WebApplicationFactory<Program>();
            _client = _factory.CreateClient();
        }
        finally { Environment.SetEnvironmentVariable(DataDirEnvVar, null); }
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        Directory.Delete(_dataDir, recursive: true);
    }

    [Fact]
    public async Task POST_api_events_writes_the_Event_first_then_the_one_off_day_its_overlap_resolution_generates()
    {
        var date = new DateOnly(2026, 10, 10);
        var window = new AvailabilityWindow(
            new WindowId("w_afternoon"), "Afternoon", new TimeOnly(13, 0), new TimeOnly(17, 0), TagSet.Empty);
        await WriteAsync(new OverridesWrite([new DateOverride(date, [window], null)]));

        var response = await _client.PostAsJsonAsync("/api/events", new
        {
            date = "2026-10-10",
            name = "Sam's tournament",
            start = "14:00",
            end = "16:00",
            resolutions = new[] { new { windowId = window.Id.Value, resolution = "split" } },
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var store = _factory.Services.GetRequiredService<IStore>();
        Assert.Single(store.Read().Events);
        Assert.Equal(2, Assert.Single(store.Read().Overrides).Windows.Count);
    }

    [Fact]
    public async Task GET_api_events_overlap_check_names_every_Window_the_proposed_Event_overlaps_partial_overlaps_included()
    {
        var date = new DateOnly(2026, 10, 10);
        var partial = new AvailabilityWindow(
            new WindowId("w_partial"), "Afternoon", new TimeOnly(13, 0), new TimeOnly(15, 0), TagSet.Empty);
        var separate = new AvailabilityWindow(
            new WindowId("w_separate"), "Evening", new TimeOnly(17, 0), new TimeOnly(19, 0), TagSet.Empty);
        await WriteAsync(new OverridesWrite([new DateOverride(date, [partial, separate], null)]));

        var overlaps = await _client.GetFromJsonAsync<AvailabilityWindow[]>(
            "/api/events/overlap-check?date=2026-10-10&start=14:00&end=16:00");

        Assert.Equal([partial.Id], Assert.IsType<AvailabilityWindow[]>(overlaps).Select(window => window.Id));
    }

    [Fact]
    public async Task PUT_api_event_exceptions_date_prototypeId_records_a_move_as_an_edit_not_as_delete_plus_create_and_stamps_no_Override()
    {
        var response = await _client.PutAsJsonAsync(
            "/api/event-exceptions/2026-10-10/ep_ministry",
            new { deleted = false, start = "14:00", end = "16:00" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var store = _factory.Services.GetRequiredService<IStore>();
        var exception = Assert.Single(store.Read().EventExceptions);
        Assert.False(exception.Deleted);
        Assert.Equal(new TimeOnly(14, 0), exception.Start);
        Assert.Equal(new TimeOnly(16, 0), exception.End);
        Assert.Empty(store.Read().Overrides);
    }

    [Fact]
    public async Task DELETE_api_event_exceptions_date_prototypeId_on_an_instance_the_active_Pattern_no_longer_assumes_matches_nothing_and_is_not_an_error()
    {
        var response = await _client.DeleteAsync("/api/event-exceptions/2026-10-10/ep_no_longer_assumed");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(_factory.Services.GetRequiredService<IStore>().Read().EventExceptions);
    }

    private async Task WriteAsync(params object[] writes)
    {
        var store = _factory.Services.GetRequiredService<IStore>();
        await store.MutateAsync<Never>(_ => OneOf<StoreMutation, Never>.FromT0(new StoreMutation(writes)), CancellationToken.None);
    }
}
