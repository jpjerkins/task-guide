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

public sealed class OverrideEndpointsTests : IDisposable
{
    private const string DataDirEnvVar = "Storage__DataDir";
    private readonly string _dataDir = Directory.CreateTempSubdirectory("taskguide-api-tests-").FullName;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public OverrideEndpointsTests()
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
    public async Task POST_api_overrides_over_a_range_writes_one_Override_per_date()
    {
        var template = new DayTemplate(new DayTemplateId("dt_01ARZ3NDEKTSV4RRFFQ69G5FAV"), "Weekend", [Window("w_weekend")], []);
        await WriteAsync(new DayTemplatesWrite([template]));

        var response = await _client.PostAsJsonAsync("/api/overrides", new { from = "2026-12-24", to = "2026-12-26", templateId = template.Id.Value });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(3, _factory.Services.GetRequiredService<IStore>().Read().Overrides.Count);
    }

    [Fact]
    public async Task GET_api_overrides_clobber_check_names_every_date_in_the_range_that_already_has_one()
    {
        await WriteAsync(new OverridesWrite([
            new DateOverride(new DateOnly(2026, 12, 24), [], null),
            new DateOverride(new DateOnly(2026, 12, 26), [], null),
        ]));

        var dates = await _client.GetFromJsonAsync<DateOnly[]>("/api/overrides/clobber-check?from=2026-12-24&to=2026-12-27");

        Assert.Equal([new DateOnly(2026, 12, 24), new DateOnly(2026, 12, 26)], Assert.IsType<DateOnly[]>(dates));
    }

    [Fact]
    public async Task PATCH_api_overrides_date_on_a_stamped_date_makes_it_a_one_off_day_and_the_use_record_survives()
    {
        var template = new DayTemplate(new DayTemplateId("dt_01ARZ3NDEKTSV4RRFFQ69G5FAV"), "Christmas", [Window("w_original")], []);
        var date = new DateOnly(2026, 12, 25);
        await WriteAsync(new OverridesWrite([DayTemplateLifecycle.Stamp(date, template)]));

        var response = await _client.PatchAsJsonAsync($"/api/overrides/{date:yyyy-MM-dd}", new { windows = new[] { Window("w_edited") } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var edited = Assert.Single(_factory.Services.GetRequiredService<IStore>().Read().Overrides);
        Assert.Equal("w_edited", Assert.Single(edited.Windows).Id.Value);
        Assert.Equal(new DayTemplateUse(template.Id, template.Name), edited.Used);
    }

    private async Task WriteAsync(params object[] writes)
    {
        var store = _factory.Services.GetRequiredService<IStore>();
        await store.MutateAsync<Never>(_ => OneOf<StoreMutation, Never>.FromT0(new StoreMutation(writes)), CancellationToken.None);
    }

    private static AvailabilityWindow Window(string id) => new(new WindowId(id), "Family time", new TimeOnly(10, 0), new TimeOnly(20, 0), TagSet.Empty);
}
