using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using Xunit;

namespace TaskGuide.Api.Tests;

/// <summary>
/// The walking skeleton's slice (#51): a Task is a title and a Duration. The walking skeleton's
/// <c>POST /api/tasks</c> and <c>GET /api/tasks</c>, plus the #138 Duration repair route, are real;
/// the remaining Task endpoints are outside this fixture's scope.
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
        Assert.Equal("30", body.GetProperty("duration").GetString());
        Assert.Contains(id, response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task post_api_tasks_snaps_raw_duration_minutes_up_to_the_declared_bucket()
    {
        var response = await _client.PostAsJsonAsync("/api/tasks", new { title = "Sort the garage", duration = 45 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("60", body.GetProperty("duration").GetString());

        var stored = Assert.Single(_factory.Services.GetRequiredService<IStore>().Read().Tasks);
        Assert.Equal("60", stored.Tags.SingleOn(KnownDimensions.Duration)?.Value);
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
    public async Task A_Task_read_returns_the_longer_Duration_bucket_verbatim()
    {
        await SeedTasksAsync(Task("t_longer", "longer"));

        var list = await _client.GetFromJsonAsync<JsonElement>("/api/tasks");

        Assert.Equal("longer", Assert.Single(list.EnumerateArray()).GetProperty("duration").GetString());
    }

    [Fact]
    public async Task A_tag_declared_Event_obligation_appears_in_the_runtime_task_list_without_being_stored()
    {
        var @event = new Event(
            new EventId("evt_01ARZ3NDEKTSV4RRFFQ69G5FAX"),
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30),
            "Family trip",
            new TimeOnly(9, 0),
            new TimeOnly(10, 0),
            new TagSet(new Dictionary<DimensionId, IReadOnlyList<TagValue>>(), [new LooseTag("timeoff")]),
            AbsenceNotice: null);
        var store = _factory.Services.GetRequiredService<IStore>();
        await store.MutateAsync<Never>(
            _ => new StoreMutation([new EventsWrite([@event])]),
            CancellationToken.None);

        var list = await _client.GetFromJsonAsync<JsonElement>("/api/tasks");

        Assert.Equal("Ask off work", Assert.Single(list.EnumerateArray()).GetProperty("title").GetString());
        Assert.Empty(store.Read().Tasks);
    }

    [Fact]
    public async Task GET_api_tasks_with_status_unprocessed_returns_only_Unprocessed_Tasks()
    {
        await _client.PostAsJsonAsync("/api/tasks", new { title = "Active task", duration = 30 });
        await _client.PostAsJsonAsync("/api/capture", new { title = "Needs processing", duration = (int?)null, source = "in-app" });

        var list = await _client.GetFromJsonAsync<JsonElement>("/api/tasks?status=unprocessed");

        Assert.Single(list.EnumerateArray());
        Assert.Equal("Needs processing", list[0].GetProperty("title").GetString());
    }

    [Fact]
    public async Task GET_api_tasks_carries_derived_status_and_fit_inputs_with_absence_distinct_from_zero()
    {
        await _client.PostAsJsonAsync("/api/tasks", new { title = "Active task", duration = 30 });
        await _client.PostAsJsonAsync("/api/capture", new { title = "Needs processing", duration = (int?)null, source = "in-app" });

        var list = await _client.GetFromJsonAsync<JsonElement>("/api/tasks");
        var active = list.EnumerateArray().Single(task => task.GetProperty("title").GetString() == "Active task");
        var unprocessed = list.EnumerateArray().Single(task => task.GetProperty("title").GetString() == "Needs processing");

        Assert.Equal("active", active.GetProperty("status").GetString());
        Assert.Equal(0, active.GetProperty("opportunities").GetInt32());
        Assert.Equal(0, active.GetProperty("patternWeekCount").GetInt32());
        Assert.Equal("orphan", active.GetProperty("zeroKind").GetString());

        Assert.Equal("unprocessed", unprocessed.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, unprocessed.GetProperty("opportunities").ValueKind);
        Assert.Equal(JsonValueKind.Null, unprocessed.GetProperty("patternWeekCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, unprocessed.GetProperty("zeroKind").ValueKind);
    }

    [Fact]
    public async Task GET_api_tasks_reports_unknown_opportunities_for_an_unavailable_fetched_dimension()
    {
        var weatherTask = new TaskItem(
            new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5FAW"),
            "Weather task",
            Notes: null,
            new TagSet(new Dictionary<DimensionId, IReadOnlyList<TagValue>>
            {
                [KnownDimensions.Duration] = [new TagValue("30")],
                [KnownDimensions.Weather] = [new TagValue("wet")],
            }, LooseTags: []),
            Deadline: null,
            Defer: null,
            Postpone: null,
            Recurrence: null,
            DateTimeOffset.UtcNow);
        await SeedTasksAsync(weatherTask);

        var list = await _client.GetFromJsonAsync<JsonElement>("/api/tasks");
        var response = Assert.Single(list.EnumerateArray());

        Assert.Equal("active", response.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, response.GetProperty("opportunities").ValueKind);
        Assert.Equal("unknown", response.GetProperty("zeroKind").GetString());
    }

    [Fact]
    public async Task GET_api_tasks_reports_done_without_fit_inputs_when_completion_precedes_unprocessed()
    {
        var capture = await _client.PostAsJsonAsync("/api/capture", new { title = "Completed capture", duration = (int?)null, source = "in-app" });
        var captured = await capture.Content.ReadFromJsonAsync<JsonElement>();
        var taskId = new TaskId(captured.GetProperty("id").GetString()!);
        var store = _factory.Services.GetRequiredService<IStore>();
        await store.MutateAsync<Never>(
            _ => new StoreMutation([new CompletionLogWrite(new CompletionLog(
                taskId,
                [new CompletionEntry(null, DateTimeOffset.UtcNow)]))]),
            CancellationToken.None);

        var response = await _client.GetAsync("/api/tasks");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<JsonElement>();
        var task = Assert.Single(list.EnumerateArray());

        Assert.Equal("done", task.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, task.GetProperty("opportunities").ValueKind);
        Assert.Equal(JsonValueKind.Null, task.GetProperty("patternWeekCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, task.GetProperty("zeroKind").ValueKind);
    }

    [Fact]
    public async Task GET_api_tasks_carries_the_list_s_defer_postpone_deadline_eligibility_recurring_and_derived_operands()
    {
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        var deferred = Task("t_01ARZ3NDEKTSV4RRFFQ69G5FAW") with
        {
            Deadline = tomorrow.AddDays(7),
            Defer = new AbsoluteDefer(tomorrow),
        };
        var postponed = Task("t_01ARZ3NDEKTSV4RRFFQ69G5FAX") with { Postpone = tomorrow };
        var recurring = Task("t_01ARZ3NDEKTSV4RRFFQ69G5FAY") with
        {
            Recurrence = new Recurrence(RecurrenceAnchor.Calendar, new EveryNDays(1), tomorrow),
        };
        var derived = Task("t_derived_absence_event_1") with
        {
            Provenance = new DerivedProvenance(new RuleId("absence"), "event_1"),
        };
        await SeedTasksAsync(deferred, postponed, recurring, derived);

        var list = await _client.GetFromJsonAsync<JsonElement>("/api/tasks");
        var deferredResponse = list.EnumerateArray().Single(task => task.GetProperty("id").GetString() == deferred.Id.Value);
        var postponedResponse = list.EnumerateArray().Single(task => task.GetProperty("id").GetString() == postponed.Id.Value);
        var recurringResponse = list.EnumerateArray().Single(task => task.GetProperty("id").GetString() == recurring.Id.Value);
        var derivedResponse = list.EnumerateArray().Single(task => task.GetProperty("id").GetString() == derived.Id.Value);

        Assert.Equal(tomorrow.ToString("yyyy-MM-dd"), deferredResponse.GetProperty("defer").GetString());
        Assert.Equal(tomorrow.AddDays(7).ToString("yyyy-MM-dd"), deferredResponse.GetProperty("deadline").GetString());
        Assert.False(deferredResponse.GetProperty("eligible").GetBoolean());
        Assert.Equal(JsonValueKind.Null, deferredResponse.GetProperty("postpone").ValueKind);

        Assert.Equal(tomorrow.ToString("yyyy-MM-dd"), postponedResponse.GetProperty("postpone").GetString());
        Assert.False(postponedResponse.GetProperty("eligible").GetBoolean());
        Assert.True(recurringResponse.GetProperty("recurring").GetBoolean());
        Assert.True(derivedResponse.GetProperty("derived").GetBoolean());
    }

    [Fact]
    public async Task GET_api_tasks_keeps_an_Unprocessed_Task_with_an_unanchored_offset_Defer_readable()
    {
        var task = Task("t_01ARZ3NDEKTSV4RRFFQ69G5FAW") with
        {
            Tags = TagSet.Empty,
            Defer = new OffsetDefer(new BeforeOffset(1, OffsetUnit.Days)),
        };
        await SeedTasksAsync(task);

        var response = await _client.GetAsync("/api/tasks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<JsonElement>();
        var body = Assert.Single(list.EnumerateArray());
        Assert.Equal("unprocessed", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("defer").ValueKind);
    }

    [Fact]
    public async Task GET_api_tasks_status_orphan_returns_only_orphan_Tasks()
    {
        await _client.PostAsJsonAsync("/api/tasks", new { title = "Orphan task", duration = 30 });
        await _client.PostAsJsonAsync("/api/capture", new { title = "Unprocessed task", duration = (int?)null, source = "in-app" });

        var list = await _client.GetFromJsonAsync<JsonElement>("/api/tasks?status=orphan");

        var task = Assert.Single(list.EnumerateArray());
        Assert.Equal("Orphan task", task.GetProperty("title").GetString());
        Assert.Equal("orphan", task.GetProperty("zeroKind").GetString());
    }

    [Fact]
    public async Task GET_api_tasks_id_returns_notes_Deadline_Dimension_values_loose_Tags_and_fit_inputs()
    {
        var deadline = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5);
        var task = Task("t_01ARZ3NDEKTSV4RRFFQ69G5FAW") with
        {
            Notes = "Use the brass screws",
            Deadline = deadline,
            Tags = new TagSet(
                new Dictionary<DimensionId, IReadOnlyList<TagValue>>
                {
                    [KnownDimensions.Duration] = [new TagValue("30")],
                    [KnownDimensions.Location] = [new TagValue("garage")],
                },
                [new LooseTag("garge")]),
        };
        await SeedTasksAsync(task);

        var response = await _client.GetAsync($"/api/tasks/{task.Id.Value}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Use the brass screws", body.GetProperty("notes").GetString());
        Assert.Equal(deadline.ToString("yyyy-MM-dd"), body.GetProperty("deadline").GetString());
        Assert.Equal("30", body.GetProperty("dimensions").GetProperty(KnownDimensions.Duration.Value)[0].GetString());
        Assert.Equal("garage", body.GetProperty("dimensions").GetProperty(KnownDimensions.Location.Value)[0].GetString());
        Assert.Equal("garge", Assert.Single(body.GetProperty("looseTags").EnumerateArray()).GetString());
        Assert.Equal("active", body.GetProperty("status").GetString());
        Assert.Equal(0, body.GetProperty("opportunities").GetInt32());
        Assert.Equal(0, body.GetProperty("patternWeekCount").GetInt32());
        Assert.Equal("orphan", body.GetProperty("zeroKind").GetString());
    }

    [Fact]
    public async Task GET_api_tasks_id_returns_404_for_a_well_formed_id_that_does_not_exist()
    {
        var task = Task("t_01ARZ3NDEKTSV4RRFFQ69G5FAW");
        await SeedTasksAsync(task);

        var response = await _client.GetAsync("/api/tasks/t_01ARZ3NDEKTSV4RRFFQ69G5FBX");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
        var derived = Task("t_derived_absence_event_1") with
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

    [Fact]
    public async Task PUT_api_tasks_id_postpone_postpones_a_plain_Task()
    {
        var task = Task("t_01ARZ3NDEKTSV4RRFFQ69G5FAW");
        await SeedTasksAsync(task);

        var response = await _client.PutAsJsonAsync($"/api/tasks/{task.Id.Value}/postpone", new { date = new DateOnly(2026, 9, 8) });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task PUT_api_tasks_id_postpone_rejects_a_malformed_Task_id()
    {
        var response = await _client.PutAsJsonAsync("/api/tasks/t_derived_absence_/postpone", new { date = new DateOnly(2026, 9, 8) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PATCH_api_tasks_id_is_refused_on_a_derived_Task()
    {
        var derived = Task("t_derived_absence_event_1") with
        {
            Provenance = new DerivedProvenance(new RuleId("absence"), "event_1"),
        };
        await SeedTasksAsync(derived);

        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/api/tasks/{derived.Id.Value}")
        {
            Content = JsonContent.Create(new { date = new DateOnly(2026, 9, 8) }),
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PATCH_api_tasks_id_defers_a_plain_Task()
    {
        var task = Task("t_01ARZ3NDEKTSV4RRFFQ69G5FAW");
        await SeedTasksAsync(task);

        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/api/tasks/{task.Id.Value}")
        {
            Content = JsonContent.Create(new { date = new DateOnly(2026, 9, 8) }),
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task PATCH_api_tasks_id_rejects_a_malformed_Task_id()
    {
        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, "/api/tasks/t_derived_absence_")
        {
            Content = JsonContent.Create(new { date = new DateOnly(2026, 9, 8) }),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Completing_a_derived_Task_writes_its_ruleId_triggerId_due_derived_completion_fact()
    {
        var due = new DateOnly(2026, 9, 8);
        var derived = Task("t_derived_absence_event_1") with
        {
            Deadline = due,
            Provenance = new DerivedProvenance(new RuleId("absence"), "event_1"),
        };
        await SeedTasksAsync(derived);

        var response = await _client.PostAsync($"/api/tasks/{derived.Id.Value}/completions", content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var derivedCompletion = Assert.Single(_factory.Services.GetRequiredService<IStore>().Read().DerivedCompletions);
        Assert.Equal(new RuleId("absence"), derivedCompletion.RuleId);
        Assert.Equal("event_1", derivedCompletion.TriggerId);
        Assert.Equal(due, derivedCompletion.Due);
    }

    [Fact]
    public async Task POST_api_tasks_id_completions_rejects_a_malformed_Task_id()
    {
        var response = await _client.PostAsync("/api/tasks/t_derived_absence_/completions", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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

    private static TaskItem Task(string id, string duration) => new(
        new TaskId(id),
        "Seeded task",
        Notes: null,
        new TagSet(new Dictionary<DimensionId, IReadOnlyList<TagValue>>
        {
            [KnownDimensions.Duration] = [new TagValue(duration)],
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
