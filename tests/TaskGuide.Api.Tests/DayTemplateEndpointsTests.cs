using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OneOf;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.Domain.Time;
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

    [Fact]
    public async Task GET_api_day_templates_returns_every_Day_template_with_its_Windows_and_Event_prototypes()
    {
        var withShape = new DayTemplate(
            Volleyball, "Volleyball", [Window("w_practice", "Practice")], [Prototype("ep_dinner", "Team dinner")]);
        var bare = new DayTemplate(Other, "Other", [], []);
        await WriteAsync(new DayTemplatesWrite([withShape, bare]));

        var response = await _client.GetFromJsonAsync<JsonElement>("/api/day-templates");

        var array = response.EnumerateArray().ToArray();
        Assert.Equal(2, array.Length);
        var shaped = array.Single(template => template.GetProperty("id").GetString() == Volleyball.Value);
        Assert.Equal("Practice", shaped.GetProperty("windows")[0].GetProperty("name").GetString());
        Assert.Equal("Team dinner", shaped.GetProperty("eventPrototypes")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task GET_api_day_templates_id_returns_one_template_and_404_for_an_unknown_id()
    {
        var template = new DayTemplate(Volleyball, "Volleyball", [], []);
        await WriteAsync(new DayTemplatesWrite([template]));

        var found = await _client.GetAsync($"/api/day-templates/{Volleyball.Value}");
        var notFound = await _client.GetAsync($"/api/day-templates/{Missing.Value}");

        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        var body = await found.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Volleyball", body.GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
    }

    [Fact]
    public async Task DayTemplateResponse_unused_is_derived_on_read_and_is_false_while_a_dormant_Pattern_references_it()
    {
        var referencedByDormant = new DayTemplate(Used, "Referenced by dormant", [], []);
        var untouched = new DayTemplate(Unused, "Untouched", [], []);
        var activeTemplate = new DayTemplate(Volleyball, "Active", [], []);
        var active = Pattern("p_active", "Active", activeTemplate.Id);
        var dormant = Pattern("p_dormant", "Dormant", referencedByDormant.Id);
        await WriteAsync(
            new DayTemplatesWrite([referencedByDormant, untouched, activeTemplate]),
            new PatternsWrite(new PatternBook(active.Id, [active, dormant])));

        var response = await _client.GetFromJsonAsync<JsonElement>("/api/day-templates");

        var array = response.EnumerateArray().ToArray();
        Assert.False(array.Single(t => t.GetProperty("id").GetString() == referencedByDormant.Id.Value).GetProperty("unused").GetBoolean());
        Assert.True(array.Single(t => t.GetProperty("id").GetString() == untouched.Id.Value).GetProperty("unused").GetBoolean());
    }

    [Fact]
    public async Task GET_api_day_templates_id_windows_windowId_preview_counts_the_eligible_Tasks_the_Window_would_admit_on_a_date_and_names_the_first_four()
    {
        var window = Window("w_01ARZ3NDEKTSV4RRFFQ69G5FAV", "Practice", new TimeOnly(10, 0), new TimeOnly(11, 0));
        var template = new DayTemplate(Volleyball, "Volleyball", [window], []);
        var tasks = new[]
        {
            TaskWithDuration("t_01ARZ3NDEKTSV4RRFFQ69G5FA1", "Fit A", "30"),
            TaskWithDuration("t_01ARZ3NDEKTSV4RRFFQ69G5FA2", "Fit B", "10"),
            TaskWithDuration("t_01ARZ3NDEKTSV4RRFFQ69G5FA3", "Fit C", "2"),
            TaskWithDuration("t_01ARZ3NDEKTSV4RRFFQ69G5FA4", "Fit D", "60"),
            TaskWithDuration("t_01ARZ3NDEKTSV4RRFFQ69G5FA5", "Fit E", "2"),
            TaskWithDuration("t_01ARZ3NDEKTSV4RRFFQ69G5FA6", "Too Long", "longer"),
            TaskWithoutDuration("t_01ARZ3NDEKTSV4RRFFQ69G5FA7", "No Duration Yet"),
        };
        await WriteAsync(new DayTemplatesWrite([template]), new TasksWrite(tasks));

        var response = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/day-templates/{Volleyball.Value}/windows/{window.Id.Value}/preview?date=2026-09-07");

        Assert.Equal(5, response.GetProperty("count").GetInt32());
        var titles = response.GetProperty("titles").EnumerateArray().Select(value => value.GetString() ?? "").ToArray();
        Assert.Equal(["Fit A", "Fit B", "Fit C", "Fit D"], titles);
    }

    [Fact]
    public async Task GET_api_day_templates_id_windows_windowId_preview_evaluates_eligibility_on_the_previewed_date_not_today()
    {
        var window = Window("w_01ARZ3NDEKTSV4RRFFQ69G5FAV", "Practice", new TimeOnly(10, 0), new TimeOnly(11, 0));
        var template = new DayTemplate(Volleyball, "Volleyball", [window], []);
        var deferDate = new DateOnly(2026, 9, 10);
        var task = TaskWithDurationAndDefer("t_01ARZ3NDEKTSV4RRFFQ69G5FA1", "Fit A", "30", deferDate);
        await WriteAsync(new DayTemplatesWrite([template]), new TasksWrite([task]));

        var onDefer = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/day-templates/{Volleyball.Value}/windows/{window.Id.Value}/preview?date=2026-09-10");
        var beforeDefer = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/day-templates/{Volleyball.Value}/windows/{window.Id.Value}/preview?date=2026-09-07");

        Assert.Contains("Fit A", onDefer.GetProperty("titles").EnumerateArray().Select(v => v.GetString()));
        Assert.DoesNotContain("Fit A", beforeDefer.GetProperty("titles").EnumerateArray().Select(v => v.GetString()));
    }

    [Fact]
    public async Task GET_api_day_templates_id_affected_dates_names_the_next_fortnights_dates_the_template_governs_and_excludes_Overridden_ones()
    {
        var boundary = new DayBoundary(TimeZoneInfo.FindSystemTimeZoneById(DayBoundary.ZoneId));
        var today = boundary.DateOf(DateTimeOffset.UtcNow);
        var target = new DayTemplate(Volleyball, "Volleyball", [], []);
        var other = new DayTemplate(Other, "Other", [], []);
        var active = Pattern("p_active", "Active", target.Id);
        await WriteAsync(
            new DayTemplatesWrite([target, other]),
            new PatternsWrite(new PatternBook(active.Id, [active])),
            new OverridesWrite([new DateOverride(today, [], new DayTemplateUse(target.Id, target.Name))]));

        var response = await _client.GetFromJsonAsync<JsonElement>($"/api/day-templates/{Volleyball.Value}/affected-dates");

        var dates = response.EnumerateArray().Select(value => DateOnly.Parse(value.GetString() ?? throw new InvalidOperationException("An affected date must be a string"))).ToArray();
        Assert.DoesNotContain(today, dates);
        Assert.Contains(today.AddDays(7), dates);
        Assert.Equal(13, dates.Length);
        Assert.Equal(dates.OrderBy(date => date).ToArray(), dates);
    }

    private async Task WriteAsync(params object[] writes)
    {
        var store = _factory.Services.GetRequiredService<IStore>();
        await store.MutateAsync<Never>(_ => OneOf<StoreMutation, Never>.FromT0(new StoreMutation(writes)), CancellationToken.None);
    }

    private static AvailabilityWindow Window(string id, string name) => new(
        new WindowId(id), name, new TimeOnly(10, 0), new TimeOnly(20, 0), TagSet.Empty);

    private static AvailabilityWindow Window(string id, string name, TimeOnly start, TimeOnly end) => new(
        new WindowId(id), name, start, end, TagSet.Empty);

    private static EventPrototype Prototype(string id, string name) => new(
        new EventPrototypeId(id), name, new TimeOnly(18, 0), new TimeOnly(19, 0), TagSet.Empty, null);

    private static Pattern Pattern(string id, string name, DayTemplateId templateId) => new(
        new PatternId(id), name, [.. Enumerable.Repeat(templateId, 7)]);

    private static TaskItem TaskWithDuration(string id, string title, string duration) => new(
        new TaskId(id), title, null,
        new TagSet(new Dictionary<DimensionId, IReadOnlyList<TagValue>>
        {
            [KnownDimensions.Duration] = [new TagValue(duration)],
        }, []),
        null, null, null, null, DateTimeOffset.UtcNow);

    private static TaskItem TaskWithDurationAndDefer(string id, string title, string duration, DateOnly deferDate) => new(
        new TaskId(id), title, null,
        new TagSet(new Dictionary<DimensionId, IReadOnlyList<TagValue>>
        {
            [KnownDimensions.Duration] = [new TagValue(duration)],
        }, []),
        null, new AbsoluteDefer(deferDate), null, null, DateTimeOffset.UtcNow);

    private static TaskItem TaskWithoutDuration(string id, string title) => new(
        new TaskId(id), title, null, TagSet.Empty, null, null, null, null, DateTimeOffset.UtcNow);
}
