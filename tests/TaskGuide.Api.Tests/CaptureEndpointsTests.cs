using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace TaskGuide.Api.Tests;

public sealed class CaptureEndpointsTests : IDisposable
{
    private const string DataDirEnvVar = "Storage__DataDir";
    private readonly string _dataDir = Directory.CreateTempSubdirectory("taskguide-capture-api-tests-").FullName;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public CaptureEndpointsTests()
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
    public async Task Capture_with_a_Duration_produces_an_Active_Task()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/capture/",
            new { title = "Repair the gate", duration = 45, source = "quick-task-shortcut" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var task = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Repair the gate", task.GetProperty("title").GetString());
        Assert.Equal("60", task.GetProperty("duration").GetString());
    }

    [Fact]
    public async Task Capturing_a_Duration_over_60_minutes_returns_the_longer_bucket_verbatim()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/capture/",
            new { title = "Reorganize the workshop", duration = 61, source = "quick-task-shortcut" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var task = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("longer", task.GetProperty("duration").GetString());
    }

    [Fact]
    public async Task Capturing_without_a_Duration_returns_null_and_task_read_remains_null()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/capture/",
            new { title = "Unprocessed capture", duration = (int?)null, source = "in-app" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var captured = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, captured.GetProperty("duration").ValueKind);

        var id = captured.GetProperty("id").GetString();
        var tasks = await _client.GetFromJsonAsync<JsonElement>("/api/tasks");
        var read = Assert.Single(tasks.EnumerateArray(), task => task.GetProperty("id").GetString() == id);
        Assert.Equal(JsonValueKind.Null, read.GetProperty("duration").ValueKind);
    }
}
