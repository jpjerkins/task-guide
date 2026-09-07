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

public sealed class RightNowEndpointsTests : IDisposable
{
    private const string DataDirEnvVar = "Storage__DataDir";
    private readonly string _dataDir = Directory.CreateTempSubdirectory("taskguide-right-now-api-tests-").FullName;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public RightNowEndpointsTests()
    {
        Environment.SetEnvironmentVariable(DataDirEnvVar, _dataDir);
        try
        {
            _factory = new WebApplicationFactory<Program>();
            _client = _factory.CreateClient();
        }
        finally
        {
            Environment.SetEnvironmentVariable(DataDirEnvVar, null);
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        Directory.Delete(_dataDir, recursive: true);
    }

    [Fact]
    public async Task PUT_api_right_now_matching_on_writes_through_to_that_dates_Override_and_does_not_stack()
    {
        var date = new DateOnly(2030, 1, 7);
        var window = new AvailabilityWindow(
            new WindowId("w_evening"), "Evening", new TimeOnly(18, 0), new TimeOnly(19, 0), TagSet.Empty);
        await SeedScheduleAsync(window);

        var first = await _client.PutAsJsonAsync("/api/right-now/matching-on", new
        {
            date,
            windowId = window.Id.Value,
            dimensions = new Dictionary<string, string[]> { ["location"] = ["garage"] },
        });

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        var written = Assert.Single(_factory.Services.GetRequiredService<IStore>().Read().Overrides);
        Assert.Equal(date, written.Date);
        Assert.Equal(new TagValue("garage"), Assert.Single(Assert.Single(written.Windows).Tags.On(new("location"))));

        var second = await _client.PutAsJsonAsync("/api/right-now/matching-on", new
        {
            date,
            windowId = window.Id.Value,
            dimensions = new Dictionary<string, string[]> { ["location"] = ["outside"] },
        });

        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        var overrideAfterSecondAdjustment = Assert.Single(_factory.Services.GetRequiredService<IStore>().Read().Overrides);
        Assert.Equal(new TagValue("outside"), Assert.Single(Assert.Single(overrideAfterSecondAdjustment.Windows).Tags.On(new("location"))));
    }

    [Fact]
    public async Task PUT_api_right_now_matching_on_is_refused_on_a_landing_page_past_its_Reminders_day_boundary()
    {
        await SeedScheduleAsync(new AvailabilityWindow(
            new WindowId("w_evening"), "Evening", new TimeOnly(18, 0), new TimeOnly(19, 0), TagSet.Empty));

        var response = await _client.PutAsJsonAsync("/api/right-now/matching-on", new
        {
            date = new DateOnly(2020, 1, 7),
            windowId = "w_evening",
            dimensions = new Dictionary<string, string[]> { ["location"] = ["garage"] },
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(_factory.Services.GetRequiredService<IStore>().Read().Overrides);
    }

    [Fact]
    public async Task PUT_api_right_now_matching_on_refuses_multiple_values_for_an_ordinal_ceiling()
    {
        await SeedScheduleAsync(new AvailabilityWindow(
            new WindowId("w_evening"), "Evening", new TimeOnly(18, 0), new TimeOnly(19, 0), TagSet.Empty));

        var response = await _client.PutAsJsonAsync("/api/right-now/matching-on", new
        {
            date = new DateOnly(2030, 1, 7),
            windowId = "w_evening",
            dimensions = new Dictionary<string, string[]> { ["energy"] = ["low", "high"] },
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(_factory.Services.GetRequiredService<IStore>().Read().Overrides);
    }

    private async Task SeedScheduleAsync(AvailabilityWindow window)
    {
        var template = new DayTemplate(new DayTemplateId("dt_everyday"), "Everyday", [window], []);
        var pattern = new Pattern(
            new PatternId("p_everyday"),
            "Everyday",
            Enumerable.Repeat(template.Id, 7).ToArray());
        var store = _factory.Services.GetRequiredService<IStore>();

        await store.MutateAsync<Never>(
            _ => OneOf<StoreMutation, Never>.FromT0(new StoreMutation([
                new DayTemplatesWrite([template]),
                new PatternsWrite(new PatternBook(pattern.Id, [pattern])),
            ])),
            CancellationToken.None);
    }
}
