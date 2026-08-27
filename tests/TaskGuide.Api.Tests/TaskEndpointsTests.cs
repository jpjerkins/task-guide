using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace TaskGuide.Api.Tests;

/// <summary>
/// The walking skeleton's slice (#51): a Task is a title and a Duration. Only
/// <c>POST /api/tasks</c> and <c>GET /api/tasks</c> are real — every other Task endpoint still
/// returns 204 and is out of scope here.
/// </summary>
/// <remarks>
/// <c>Program.cs</c> reads <c>Storage:DataDir</c> from configuration on the line before
/// <c>builder.Build()</c> — earlier than <c>WithWebHostBuilder().ConfigureAppConfiguration()</c>
/// takes effect for a minimal-hosting entry point (that hook applies at <c>Build()</c>, too late
/// for a value already read). An environment variable is visible from the moment
/// <c>WebApplication.CreateBuilder</c> runs, so that's the override path that actually reaches
/// this line. <c>AssemblyInfo.cs</c> disables test parallelization so no other test in this
/// assembly observes the environment variable while it's set.
/// </remarks>
public sealed class TaskEndpointsTests : IDisposable
{
    private const string DataDirEnvVar = "Storage__DataDir";

    private readonly string _dataDir = Directory.CreateTempSubdirectory("taskguide-api-tests-").FullName;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public TaskEndpointsTests()
    {
        Environment.SetEnvironmentVariable(DataDirEnvVar, _dataDir);
        try
        {
            _factory = new WebApplicationFactory<Program>();
            _client = _factory.CreateClient(); // forces host startup now, while the env var is still set
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
    public async Task Posting_a_task_creates_it_returning_201_with_a_location_header_and_a_ulid_id()
    {
        var response = await _client.PostAsJsonAsync("/api/tasks", new { title = "Fix the shelf bracket", duration = 30 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetString()!;
        Assert.Matches("^t_[0-9A-HJKMNP-TV-Z]{26}$", id);
        Assert.Equal("Fix the shelf bracket", body.GetProperty("title").GetString());
        Assert.Equal(30, body.GetProperty("duration").GetInt32());
        Assert.Contains(id, response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task A_posted_task_appears_in_the_task_list()
    {
        await _client.PostAsJsonAsync("/api/tasks", new { title = "Take the bins out", duration = 2 });

        var list = await _client.GetFromJsonAsync<JsonElement>("/api/tasks");

        Assert.Single(list.EnumerateArray());
        Assert.Equal("Take the bins out", list[0].GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_posted_task_is_persisted_to_tasks_json_on_disk()
    {
        await _client.PostAsJsonAsync("/api/tasks", new { title = "Descale the kettle", duration = 30 });

        var onDisk = await File.ReadAllTextAsync(Path.Combine(_dataDir, "tasks.json"));
        Assert.Contains("Descale the kettle", onDisk);
    }

    [Fact]
    public async Task A_blank_title_is_rejected_with_400_not_500()
    {
        var response = await _client.PostAsJsonAsync("/api/tasks", new { title = "   ", duration = 30 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_nonpositive_duration_is_rejected_with_400()
    {
        var response = await _client.PostAsJsonAsync("/api/tasks", new { title = "Water the plants", duration = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
