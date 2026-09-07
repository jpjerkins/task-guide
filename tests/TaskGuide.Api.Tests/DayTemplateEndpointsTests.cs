using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OneOf;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using Xunit;

namespace TaskGuide.Api.Tests;

/// <summary>HTTP contract for #89's promotion, stamping, deletion, and usage-list lifecycle.</summary>
public sealed class DayTemplateEndpointsTests : IDisposable
{
    private const string DataDirEnvVar = "Storage__DataDir";
    private static readonly DayTemplateId Christmas = new("dt_01ARZ3NDEKTSV4RRFFQ69G5FAV");
    private static readonly DayTemplateId Missing = new("dt_01ARZ3NDEKTSV4RRFFQ69G5FAW");
    private static readonly DayTemplateId Used = new("dt_01ARZ3NDEKTSV4RRFFQ69G5FAX");
    private static readonly DayTemplateId Unused = new("dt_01ARZ3NDEKTSV4RRFFQ69G5FAY");
    private static readonly DayTemplateId Volleyball = new("dt_01ARZ3NDEKTSV4RRFFQ69G5FAZ");
    private static readonly DayTemplateId Other = new("dt_01ARZ3NDEKTSV4RRFFQ69G5FB0");
    private readonly string _dataDir = Directory.CreateTempSubdirectory("taskguide-api-tests-").FullName;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public DayTemplateEndpointsTests()
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
    public async Task Malformed_day_template_lifecycle_route_or_body_input_is_400()
    {
        var promote = await _client.PostAsJsonAsync("/api/overrides/not-a-date/promote", new { name = "Christmas" });
        var stamp = await _client.PutAsJsonAsync("/api/overrides/2026-12-25/stamp", new { templateId = "not-a-template" });
        var missingTemplateId = await _client.PutAsJsonAsync("/api/overrides/2026-12-25/stamp", new { });
        var delete = await _client.DeleteAsync("/api/day-templates/not-a-template");

        Assert.Equal(HttpStatusCode.BadRequest, promote.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, stamp.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingTemplateId.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);
    }

    [Fact]
    public async Task POST_api_overrides_date_promote_writes_a_new_Day_template_and_does_not_re_link_the_source_date()
    {
        var date = new DateOnly(2026, 12, 25);
        var sourceWindow = Window("w_christmas", "Family time");
        await WriteAsync(new OverridesWrite([new DateOverride(date, [sourceWindow], null)]));

        var response = await _client.PostAsJsonAsync($"/api/overrides/{date:yyyy-MM-dd}/promote", new { name = "Christmas" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var templateId = Assert.IsType<string>(body.GetProperty("id").GetString());
        Assert.StartsWith(DayTemplateId.Prefix, templateId);
        Assert.Equal("Christmas", body.GetProperty("name").GetString());
        Assert.Equal("Family time", body.GetProperty("windows")[0].GetProperty("name").GetString());
        var source = Assert.Single(_factory.Services.GetRequiredService<IStore>().Read().Overrides);
        Assert.Equal("Family time", Assert.Single(source.Windows).Name);
        Assert.Equal(templateId, Assert.IsType<DayTemplateUse>(source.Used).TemplateId.Value);
    }

    [Fact]
    public async Task PUT_api_overrides_date_stamp_copies_the_templates_shape_and_preserves_each_Windows_id()
    {
        var date = new DateOnly(2026, 12, 25);
        var template = new DayTemplate(Christmas, "Christmas", [Window("w_christmas", "Family time")], []);
        await WriteAsync(new DayTemplatesWrite([template]));

        var stamped = await _client.PutAsJsonAsync($"/api/overrides/{date:yyyy-MM-dd}/stamp", new { templateId = template.Id.Value });

        Assert.Equal(HttpStatusCode.OK, stamped.StatusCode);
        var body = await stamped.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Family time", body.GetProperty("windows")[0].GetProperty("name").GetString());
        Assert.Equal(template.Id.Value, body.GetProperty("used").GetProperty("templateId").GetString());
    }

    [Fact]
    public async Task PUT_api_overrides_date_stamp_is_refused_for_an_unknown_template_id()
    {
        var response = await _client.PutAsJsonAsync("/api/overrides/2026-12-25/stamp", new { templateId = Missing.Value });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DELETE_api_day_templates_id_is_refused_while_the_template_is_in_use_and_accepted_when_it_is_Unused()
    {
        var used = new DayTemplate(Used, "Used", [], []);
        var unused = new DayTemplate(Unused, "Unused", [], []);
        await WriteAsync(
            new DayTemplatesWrite([used, unused]),
            new OverridesWrite([new DateOverride(new DateOnly(2026, 9, 6), [], new DayTemplateUse(used.Id, used.Name))]));

        var refused = await _client.DeleteAsync($"/api/day-templates/{used.Id.Value}");
        var deleted = await _client.DeleteAsync($"/api/day-templates/{unused.Id.Value}");

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.DoesNotContain(_factory.Services.GetRequiredService<IStore>().Read().DayTemplates, template => template.Id == unused.Id);
    }

    [Fact]
    public async Task GET_api_day_templates_id_usage_names_every_Pattern_referencing_the_template_dormant_ones_included()
    {
        var template = new DayTemplate(Volleyball, "Volleyball", [], []);
        var other = new DayTemplate(Other, "Other", [], []);
        var active = Pattern("p_school", "School year", other.Id);
        var dormant = Pattern("p_summer", "Summer volleyball", template.Id);
        await WriteAsync(new DayTemplatesWrite([template, other]), new PatternsWrite(new PatternBook(active.Id, [active, dormant])));

        var response = await _client.GetFromJsonAsync<JsonElement>($"/api/day-templates/{template.Id.Value}/usage");

        Assert.Equal(["Summer volleyball"], response.EnumerateArray().Select(value => value.GetString() ?? throw new InvalidOperationException("Usage names must be strings")).ToArray());
    }

    private async Task WriteAsync(params object[] writes)
    {
        var store = _factory.Services.GetRequiredService<IStore>();
        await store.MutateAsync<Never>(_ => OneOf<StoreMutation, Never>.FromT0(new StoreMutation(writes)), CancellationToken.None);
    }

    private static AvailabilityWindow Window(string id, string name) => new(
        new WindowId(id), name, new TimeOnly(10, 0), new TimeOnly(20, 0), TagSet.Empty);

    private static Pattern Pattern(string id, string name, DayTemplateId templateId) => new(
        new PatternId(id), name, [.. Enumerable.Repeat(templateId, 7)]);
}
