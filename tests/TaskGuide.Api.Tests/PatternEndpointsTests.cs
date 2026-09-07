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
using TaskGuide.Domain.Tasks;
using Xunit;

namespace TaskGuide.Api.Tests;

/// <summary>HTTP contract for #90's Pattern lifecycle and pre-switch Drift warning.</summary>
public sealed class PatternEndpointsTests : IDisposable
{
    private const string DataDirEnvVar = "Storage__DataDir";
    private readonly string _dataDir = Directory.CreateTempSubdirectory("taskguide-api-tests-").FullName;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public PatternEndpointsTests()
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
    public async Task GET_api_patterns_active_switch_impact_returns_the_orphan_count_before_the_switch()
    {
        var admittingTemplate = new DayTemplate(new DayTemplateId("dt_admitting"), "Admitting", [Window("w_admitting")], []);
        var emptyTemplate = new DayTemplate(new DayTemplateId("dt_empty"), "Empty", [], []);
        var active = Pattern("p_01ARZ3NDEKTSV4RRFFQ69G5FAV", "Active", admittingTemplate.Id);
        var candidate = Pattern("p_01ARZ3NDEKTSV4RRFFQ69G5FAW", "Candidate", emptyTemplate.Id);
        await WriteAsync(
            new DayTemplatesWrite([admittingTemplate, emptyTemplate]),
            new PatternsWrite(new PatternBook(active.Id, [active, candidate])),
            new TasksWrite([Task("t_affected")]));

        var response = await _client.GetAsync($"/api/patterns/active/switch-impact?to={candidate.Id.Value}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("newlyOrphaned").GetInt32());
    }

    [Fact]
    public async Task DELETE_api_patterns_id_is_refused_for_the_active_Pattern()
    {
        var template = new DayTemplate(new DayTemplateId("dt_delete"), "Template", [], []);
        var active = Pattern("p_01ARZ3NDEKTSV4RRFFQ69G5FAV", "Active", template.Id);
        var dormant = Pattern("p_01ARZ3NDEKTSV4RRFFQ69G5FAW", "Dormant", template.Id);
        await WriteAsync(
            new DayTemplatesWrite([template]),
            new PatternsWrite(new PatternBook(active.Id, [active, dormant])));

        var refused = await _client.DeleteAsync($"/api/patterns/{active.Id.Value}");
        var deleted = await _client.DeleteAsync($"/api/patterns/{dormant.Id.Value}");

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.DoesNotContain(_factory.Services.GetRequiredService<IStore>().Read().Patterns.Patterns, pattern => pattern.Id.Equals(dormant.Id));
    }

    private async Task WriteAsync(params object[] writes)
    {
        var store = _factory.Services.GetRequiredService<IStore>();
        await store.MutateAsync<Never>(_ => OneOf<StoreMutation, Never>.FromT0(new StoreMutation(writes)), CancellationToken.None);
    }

    private static AvailabilityWindow Window(string id) => new(
        new WindowId(id), "Admitting", new TimeOnly(9, 0), new TimeOnly(10, 0), TagSet.Empty);

    private static Pattern Pattern(string id, string name, DayTemplateId templateId) => new(
        new PatternId(id), name, [.. Enumerable.Repeat(templateId, 7)]);

    private static TaskItem Task(string id) => new(
        new TaskId(id), "Affected", Notes: null,
        new TagSet(new Dictionary<TaskGuide.Domain.Dimensions.DimensionId, IReadOnlyList<TagValue>>
        {
            [TaskGuide.Domain.Dimensions.KnownDimensions.Duration] = [new TagValue("30")],
        }, []),
        Deadline: null, Defer: null, Postpone: null, Recurrence: null, CreatedAt: DateTimeOffset.UtcNow);
}
