using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using Xunit;

namespace TaskGuide.Api.Tests;

public sealed class TaskDurationEndpointsTests : IDisposable
{
    private const string DataDirEnvVar = "Storage__DataDir";

    private readonly string _dataDir = Directory.CreateTempSubdirectory("taskguide-api-tests-").FullName;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public TaskDurationEndpointsTests()
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

    [Theory]
    [InlineData("2")]
    [InlineData("10")]
    [InlineData("30")]
    [InlineData("60")]
    [InlineData("longer")]
    public async Task PUT_duration_accepts_each_declared_bucket(string duration)
    {
        var task = Task("t_01ARZ3NDEKTSV4RRFFQ69G5FAW");
        await SeedTasksAsync(task);

        var response = await _client.PutAsJsonAsync($"/api/tasks/{task.Id.Value}/duration", new { duration });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var stored = Assert.Single(_factory.Services.GetRequiredService<IStore>().Read().Tasks);
        Assert.Equal(duration, stored.Tags.SingleOn(KnownDimensions.Duration)?.Value);
    }

    [Fact]
    public async Task PUT_duration_replaces_only_Duration_and_is_idempotent()
    {
        var task = Task("t_01ARZ3NDEKTSV4RRFFQ69G5FAW", new TagSet(
            new Dictionary<DimensionId, IReadOnlyList<TagValue>>
            {
                [KnownDimensions.Duration] = [new TagValue("10")],
                [KnownDimensions.Location] = [new TagValue("garage")],
            },
            [new LooseTag("urgent")]));
        var other = Task("t_01ARZ3NDEKTSV4RRFFQ69G5FAX");
        await SeedTasksAsync(task, other);

        var first = await _client.PutAsJsonAsync($"/api/tasks/{task.Id.Value}/duration", new { duration = "30" });
        var afterFirst = _factory.Services.GetRequiredService<IStore>().Read();
        var second = await _client.PutAsJsonAsync($"/api/tasks/{task.Id.Value}/duration", new { duration = "30" });
        var afterSecond = _factory.Services.GetRequiredService<IStore>().Read();

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        var updated = Assert.Single(afterSecond.Tasks, candidate => candidate.Id == task.Id);
        Assert.Equal(task with
        {
            Tags = task.Tags with
            {
                Dimensions = new Dictionary<DimensionId, IReadOnlyList<TagValue>>
                {
                    [KnownDimensions.Duration] = [new TagValue("30")],
                    [KnownDimensions.Location] = [new TagValue("garage")],
                },
            },
        }, updated);
        Assert.Equal(other, Assert.Single(afterSecond.Tasks, candidate => candidate.Id == other.Id));
        Assert.Equal(updated, Assert.Single(afterFirst.Tasks, candidate => candidate.Id == task.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Longer")]
    [InlineData(" 30")]
    [InlineData("30 ")]
    [InlineData("45")]
    public async Task PUT_duration_rejects_noncanonical_buckets_with_400_without_mutation(string? duration)
    {
        var task = Task("t_01ARZ3NDEKTSV4RRFFQ69G5FAW");
        await SeedTasksAsync(task);
        var before = _factory.Services.GetRequiredService<IStore>().Read();

        var response = await _client.PutAsJsonAsync($"/api/tasks/{task.Id.Value}/duration", new { duration });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Same(before, _factory.Services.GetRequiredService<IStore>().Read());
    }

    [Fact]
    public async Task PUT_duration_rejects_malformed_id_with_400()
    {
        var response = await _client.PutAsJsonAsync("/api/tasks/t_derived_absence_/duration", new { duration = "30" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PUT_duration_refuses_absent_and_derived_tasks_with_409_without_mutation()
    {
        var derived = Task("t_derived_absence_event_1") with
        {
            Provenance = new DerivedProvenance(new RuleId("absence"), "event_1"),
        };
        await SeedTasksAsync(derived);
        var store = _factory.Services.GetRequiredService<IStore>();
        var before = store.Read();

        var absent = await _client.PutAsJsonAsync("/api/tasks/t_01ARZ3NDEKTSV4RRFFQ69G5FAW/duration", new { duration = "30" });
        var derivedResponse = await _client.PutAsJsonAsync($"/api/tasks/{derived.Id.Value}/duration", new { duration = "30" });

        Assert.Equal(HttpStatusCode.Conflict, absent.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, derivedResponse.StatusCode);
        Assert.Same(before, store.Read());
    }

    [Theory]
    [InlineData("{\"duration\":30}")]
    [InlineData("{\"duration\":")]
    public async Task PUT_duration_rejects_numeric_or_malformed_json_with_400(string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/tasks/t_01ARZ3NDEKTSV4RRFFQ69G5FAW/duration")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task SeedTasksAsync(params TaskItem[] tasks)
    {
        var store = _factory.Services.GetRequiredService<IStore>();
        await store.MutateAsync<Never>(_ => new StoreMutation([new TasksWrite(tasks)]), CancellationToken.None);
    }

    private static TaskItem Task(string id) => Task(id, TagSet.Empty);

    private static TaskItem Task(string id, TagSet tags) => new(
        new TaskId(id),
        "Seeded task",
        Notes: null,
        tags,
        Deadline: null,
        Defer: null,
        Postpone: null,
        Recurrence: null,
        DateTimeOffset.UtcNow);
}
