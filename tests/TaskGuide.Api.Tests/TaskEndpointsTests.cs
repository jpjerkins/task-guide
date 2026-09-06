using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
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
        if (!OperatingSystem.IsWindows()) Chmod(_dataDir, 0b111_101_101); // undo any chmod a test applied
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

    [Fact]
    public async Task POST_api_tasks_id_completions_is_refused_on_an_Unprocessed_Task()
    {
        var capture = await _client.PostAsJsonAsync("/api/capture", new { title = "Sort the garage", duration = (int?)null, source = "in-app" });
        var task = await capture.Content.ReadFromJsonAsync<JsonElement>();
        var id = task.GetProperty("id").GetString();

        var response = await _client.PostAsync($"/api/tasks/{id}/completions", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PUT_api_tasks_id_postpone_is_refused_on_a_recurring_Task_and_a_derived_Task()
    {
        var recurring = Task("t_01ARZ3NDEKTSV4RRFFQ69G5FAW") with
        {
            Recurrence = new Recurrence(RecurrenceAnchor.Calendar, new EveryNDays(1), null),
        };
        var derived = Task("t_01ARZ3NDEKTSV4RRFFQ69G5FAX") with
        {
            Provenance = new DerivedProvenance(new RuleId("absence"), "event_1"),
        };
        await SeedTasksAsync(recurring, derived);

        foreach (var task in new[] { recurring, derived })
        {
            var response = await _client.PutAsJsonAsync($"/api/tasks/{task.Id.Value}/postpone", new { date = new DateOnly(2026, 9, 8) });
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }
    }

    private async Task SeedTasksAsync(params TaskItem[] tasks)
    {
        var store = _factory.Services.GetRequiredService<IStore>();
        await store.MutateAsync<Never>(_ => new StoreMutation([new TasksWrite(tasks)]), CancellationToken.None);
    }

    private static TaskItem Task(string id) => new(
        new TaskId(id),
        "Seeded task",
        Notes: null,
        new TagSet(new Dictionary<DimensionId, IReadOnlyList<TagValue>>
        {
            [KnownDimensions.Duration] = [new TagValue("30")],
        }, LooseTags: []),
        Deadline: null,
        Defer: null,
        Postpone: null,
        Recurrence: null,
        DateTimeOffset.UtcNow);

    /// <summary>
    /// The exact repro reported live against the running API: chmod the data dir unwritable
    /// after boot, then POST — a failed persist must be a deliberate 503 with the exception
    /// logged, never a raw 500.
    /// </summary>
    [Fact]
    public async Task A_disk_write_failure_is_a_503_not_a_raw_500()
    {
        if (OperatingSystem.IsWindows()) return; // chmod-based denial is POSIX-specific; see HealthReporterTests

        Chmod(_dataDir, 0b101_000_000); // 500: r-x for the owner, no write — even for the owner, not root

        var response = await _client.PostAsJsonAsync("/api/tasks", new { title = "should fail", duration = 5 });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [DllImport("libc", SetLastError = true, EntryPoint = "chmod")]
    private static extern int chmod(string pathname, int mode);

    private static void Chmod(string path, int mode)
    {
        if (chmod(path, mode) != 0)
        {
            throw new IOException($"chmod({path}, {Convert.ToString(mode, 8)}) failed: {Marshal.GetLastWin32Error()}");
        }
    }
}
