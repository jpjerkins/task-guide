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
using Xunit;

namespace TaskGuide.Api.Tests;

public sealed class WindowEndpointsTests : IDisposable
{
    private const string DataDirEnvVar = "Storage__DataDir";
    private readonly string _dataDir = Directory.CreateTempSubdirectory("taskguide-api-tests-").FullName;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public WindowEndpointsTests()
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
    public async Task POST_api_day_templates_id_windows_appends_a_minted_Window_to_that_template()
    {
        var template = new DayTemplate(new DayTemplateId("dt_01ARZ3NDEKTSV4RRFFQ69G5FAT"), "Template", [], []);
        await WriteAsync(new DayTemplatesWrite([template]));

        var response = await _client.PostAsJsonAsync($"/api/day-templates/{template.Id.Value}/windows", new
        {
            name = "Morning",
            start = new TimeOnly(9, 0),
            end = new TimeOnly(10, 0),
            tags = TagSet.Empty,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.StartsWith(WindowId.Prefix, created.GetProperty("id").GetProperty("value").GetString());
        Assert.Equal("Morning", Assert.Single(_factory.Services.GetRequiredService<IStore>().Read().DayTemplates).Windows.Single().Name);
    }

    [Fact]
    public async Task PATCH_api_day_templates_id_windows_windowId_edits_that_Window_only_and_does_not_propagate_to_a_same_named_Window_in_another_template()
    {
        var first = new DayTemplate(new DayTemplateId("dt_01ARZ3NDEKTSV4RRFFQ69G5FAV"), "First", [Window("w_01ARZ3NDEKTSV4RRFFQ69G5FAV", "Evening", TagSet.Empty)], []);
        var second = new DayTemplate(new DayTemplateId("dt_01ARZ3NDEKTSV4RRFFQ69G5FAW"), "Second", [Window("w_01ARZ3NDEKTSV4RRFFQ69G5FAW", "Evening", TagSet.Empty)], []);
        await WriteAsync(new DayTemplatesWrite([first, second]));

        var response = await _client.PatchAsJsonAsync($"/api/day-templates/{first.Id.Value}/windows/w_01ARZ3NDEKTSV4RRFFQ69G5FAV", new
        {
            name = "Edited evening",
            start = new TimeOnly(18, 0),
            end = new TimeOnly(21, 0),
            tags = TagSet.Empty,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var templates = _factory.Services.GetRequiredService<IStore>().Read().DayTemplates;
        Assert.Equal("Edited evening", Assert.Single(templates.Single(template => template.Id.Equals(first.Id)).Windows).Name);
        Assert.Equal("Evening", Assert.Single(templates.Single(template => template.Id.Equals(second.Id)).Windows).Name);
    }

    [Fact]
    public async Task GET_api_day_templates_id_windows_windowId_dependents_counts_the_Tasks_that_would_be_orphaned_by_removing_a_Dimension_value_before_the_edit_is_saved_and_writes_nothing()
    {
        var garage = new TagValue("garage");
        var carrie = new TagValue("carrie");
        var windowTags = Tags((KnownDimensions.Location, garage), (KnownDimensions.WithWhom, carrie));
        var window = Window("w_01ARZ3NDEKTSV4RRFFQ69G5FAX", "Garage", windowTags);
        var activeTemplate = new DayTemplate(new DayTemplateId("dt_01ARZ3NDEKTSV4RRFFQ69G5FAX"), "Active", [window], []);
        var dormantTemplate = new DayTemplate(new DayTemplateId("dt_01ARZ3NDEKTSV4RRFFQ69G5FAY"), "Dormant", [Window("w_01ARZ3NDEKTSV4RRFFQ69G5FAY", "Garage", windowTags)], []);
        var activePattern = Pattern("p_01ARZ3NDEKTSV4RRFFQ69G5FAV", "Active", activeTemplate.Id);
        var dormantPattern = Pattern("p_01ARZ3NDEKTSV4RRFFQ69G5FAW", "Dormant", dormantTemplate.Id);
        await WriteAsync(
            new DayTemplatesWrite([activeTemplate, dormantTemplate]),
            new PatternsWrite(new PatternBook(activePattern.Id, [activePattern, dormantPattern])),
            new TasksWrite([
                Task("t_garage", Tags((KnownDimensions.Location, garage), (KnownDimensions.WithWhom, carrie), (KnownDimensions.Duration, new TagValue("30")))) with { Defer = new AbsoluteDefer(new DateOnly(2099, 1, 1)) },
                Task("t_derived", Tags((KnownDimensions.Location, garage), (KnownDimensions.WithWhom, carrie), (KnownDimensions.Duration, new TagValue("30")))) with { Provenance = new DerivedProvenance(new RuleId("r_test"), "trigger") },
                Task("t_unprocessed", Tags((KnownDimensions.Location, garage), (KnownDimensions.WithWhom, carrie))),
                Task("t_stale", Tags((KnownDimensions.Location, garage), (KnownDimensions.WithWhom, carrie), (KnownDimensions.Duration, new TagValue("30"))), DateTimeOffset.UtcNow.AddDays(-61)),
            ]));

        var store = _factory.Services.GetRequiredService<IStore>();
        var tasksBeforeRead = store.Read().Tasks;

        var response = await _client.GetAsync($"/api/day-templates/{activeTemplate.Id.Value}/windows/{window.Id.Value}/dependents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var entries = body.EnumerateArray().ToDictionary(
            entry => (entry.GetProperty("dimensionId").GetString(), entry.GetProperty("value").GetString()),
            entry => entry.GetProperty("dependentTasks").GetInt32());
        Assert.Equal(2, entries.Count);
        Assert.Equal(2, entries[(KnownDimensions.Location.Value, garage.Value)]);
        Assert.Equal(2, entries[(KnownDimensions.WithWhom.Value, carrie.Value)]);
        Assert.Equal(tasksBeforeRead, store.Read().Tasks);
    }

    [Fact]
    public async Task DELETE_api_day_templates_id_windows_windowId_removes_only_that_Window()
    {
        var first = Window("w_01ARZ3NDEKTSV4RRFFQ69G5FAT", "First", TagSet.Empty);
        var second = Window("w_01ARZ3NDEKTSV4RRFFQ69G5FAZ", "Second", TagSet.Empty);
        var template = new DayTemplate(new DayTemplateId("dt_01ARZ3NDEKTSV4RRFFQ69G5FAZ"), "Template", [first, second], []);
        await WriteAsync(new DayTemplatesWrite([template]));

        var response = await _client.DeleteAsync($"/api/day-templates/{template.Id.Value}/windows/{first.Id.Value}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(second, Assert.Single(_factory.Services.GetRequiredService<IStore>().Read().DayTemplates.Single().Windows));
    }

    private async Task WriteAsync(params object[] writes)
    {
        var store = _factory.Services.GetRequiredService<IStore>();
        await store.MutateAsync<Never>(_ => OneOf<StoreMutation, Never>.FromT0(new StoreMutation(writes)), CancellationToken.None);
    }

    private static AvailabilityWindow Window(string id, string name, TagSet tags) =>
        new(new WindowId(id), name, new TimeOnly(18, 0), new TimeOnly(21, 0), tags);

    private static Pattern Pattern(string id, string name, DayTemplateId templateId) =>
        new(new PatternId(id), name, [.. Enumerable.Repeat(templateId, 7)]);

    private static TaskItem Task(string id, TagSet tags, DateTimeOffset? createdAt = null) =>
        new(new TaskId(id), "Task", null, tags, null, null, null, null, createdAt ?? DateTimeOffset.UtcNow);

    private static TagSet Tags(params (DimensionId Id, TagValue Value)[] values) =>
        new(values.GroupBy(value => value.Id).ToDictionary(group => group.Key, group => (IReadOnlyList<TagValue>)group.Select(value => value.Value).ToArray()), []);
}
