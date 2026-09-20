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

        var response = await _client.PostAsJsonAsync("/api/overrides", new { from = "2026-12-24", to = "2026-12-26", templateId = template.Id.Value, mode = "stamp" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(3, _factory.Services.GetRequiredService<IStore>().Read().Overrides.Count);
    }

    [Fact]
    public async Task POST_api_overrides_freeze_copies_each_dates_current_shape_and_preserves_Window_ids()
    {
        var first = new DayTemplate(new DayTemplateId("dt_01ARZ3NDEKTSV4RRFFQ69G5FAV"), "Thursday", [Window("w_thursday")], []);
        var second = new DayTemplate(new DayTemplateId("dt_01ARZ3NDEKTSV4RRFFQ69G5FAW"), "Friday", [Window("w_friday")], []);
        var third = new DayTemplate(new DayTemplateId("dt_01ARZ3NDEKTSV4RRFFQ69G5FAX"), "Saturday", [Window("w_saturday")], []);
        var patternId = new PatternId("p_01ARZ3NDEKTSV4RRFFQ69G5FAV");
        var days = Enumerable.Repeat(first.Id, 7).ToArray();
        days[(int)DayOfWeek.Friday] = second.Id;
        days[(int)DayOfWeek.Saturday] = third.Id;
        await WriteAsync(
            new DayTemplatesWrite([first, second, third]),
            new PatternsWrite(new PatternBook(patternId, [new Pattern(patternId, "Week", days)])));

        var response = await _client.PostAsJsonAsync("/api/overrides", new
        {
            from = "2026-12-24",
            to = "2026-12-26",
            mode = "freeze",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var overrides = _factory.Services.GetRequiredService<IStore>().Read().Overrides;
        Assert.Equal(["w_thursday", "w_friday", "w_saturday"], overrides
            .OrderBy(overrideDay => overrideDay.Date)
            .SelectMany(overrideDay => overrideDay.Windows)
            .Select(window => window.Id.Value)
            .ToArray());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("dt_01ARZ3NDEKTSV4RRFFQ69G5FAV")]
    public async Task A_span_POST_with_no_mode_is_refused_with_400_and_writes_nothing_whatever_templateId_carries(string? templateId)
    {
        object absentModeRequest = templateId is null
            ? new { from = "2026-12-24", to = "2026-12-26" }
            : new { from = "2026-12-24", to = "2026-12-26", templateId };
        var absentModeResponse = await _client.PostAsJsonAsync("/api/overrides", absentModeRequest);
        var nullModeResponse = await _client.PostAsJsonAsync("/api/overrides", new
        {
            from = "2026-12-24",
            to = "2026-12-26",
            templateId,
            mode = (string?)null,
        });

        Assert.Equal(HttpStatusCode.BadRequest, absentModeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, nullModeResponse.StatusCode);
        var error = await absentModeResponse.Content.ReadAsStringAsync();
        Assert.Contains("stamp", error);
        Assert.Contains("freeze", error);
        Assert.Contains("blank", error);
        Assert.Empty(_factory.Services.GetRequiredService<IStore>().Read().Overrides);
    }

    [Theory]
    [InlineData("stamp", null)]
    [InlineData("freeze", "dt_01ARZ3NDEKTSV4RRFFQ69G5FAV")]
    [InlineData("blank", "dt_01ARZ3NDEKTSV4RRFFQ69G5FAV")]
    [InlineData("unrecognised", null)]
    public async Task Each_existing_400_still_refuses_stamp_with_no_templateId_a_templateId_on_freeze_or_blank_and_an_unrecognised_mode(
        string mode,
        string? templateId)
    {
        var response = await _client.PostAsJsonAsync("/api/overrides", new
        {
            from = "2026-12-24",
            to = "2026-12-26",
            templateId,
            mode,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task All_three_arms_still_succeed_when_mode_is_stated()
    {
        var template = new DayTemplate(new DayTemplateId("dt_01ARZ3NDEKTSV4RRFFQ69G5FAV"), "Every day", [Window("w_every_day")], []);
        var patternId = new PatternId("p_01ARZ3NDEKTSV4RRFFQ69G5FAV");
        await WriteAsync(
            new DayTemplatesWrite([template]),
            new PatternsWrite(new PatternBook(patternId, [new Pattern(patternId, "Every day", [.. Enumerable.Repeat(template.Id, 7)])])));

        var stamp = await _client.PostAsJsonAsync("/api/overrides", new { from = "2026-12-24", to = "2026-12-24", templateId = template.Id.Value, mode = "stamp" });
        var freeze = await _client.PostAsJsonAsync("/api/overrides", new { from = "2026-12-25", to = "2026-12-25", mode = "freeze" });
        var blank = await _client.PostAsJsonAsync("/api/overrides", new { from = "2026-12-26", to = "2026-12-26", mode = "blank" });

        Assert.Equal(HttpStatusCode.Created, stamp.StatusCode);
        Assert.Equal(HttpStatusCode.Created, freeze.StatusCode);
        Assert.Equal(HttpStatusCode.Created, blank.StatusCode);
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

    [Fact]
    public async Task DELETE_api_overrides_date_removes_the_Override()
    {
        var date = new DateOnly(2026, 12, 25);
        await WriteAsync(new OverridesWrite([new DateOverride(date, [], null)]));

        var response = await _client.DeleteAsync($"/api/overrides/{date:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(_factory.Services.GetRequiredService<IStore>().Read().Overrides);
    }

    [Fact]
    public async Task DELETE_api_overrides_date_for_a_date_with_no_Override_is_a_conflict()
    {
        var response = await _client.DeleteAsync("/api/overrides/2026-12-25");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DELETE_api_overrides_date_rejects_an_unparseable_date()
    {
        var response = await _client.DeleteAsync("/api/overrides/not-a-date");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task WriteAsync(params object[] writes)
    {
        var store = _factory.Services.GetRequiredService<IStore>();
        await store.MutateAsync<Never>(_ => OneOf<StoreMutation, Never>.FromT0(new StoreMutation(writes)), CancellationToken.None);
    }

    private static AvailabilityWindow Window(string id) => new(new WindowId(id), "Family time", new TimeOnly(10, 0), new TimeOnly(20, 0), TagSet.Empty);
}
