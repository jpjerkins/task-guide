using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.Infrastructure.Storage;
using Xunit;

namespace TaskGuide.Storage.Tests;

/// <summary>
/// Against `fixtures/data`, the golden store (`tests/TEST-INVENTORY.md`'s "Sequential ·
/// TaskGuide.Storage.Tests" section). Only the Tasks slice of the substrate is wired up for
/// the walking skeleton (#51) — every test here exercises `tasks.json` only.
/// </summary>
public sealed class JsonStoreTests : IDisposable
{
    private readonly string _dataDir = Directory.CreateTempSubdirectory("taskguide-storage-tests-").FullName;

    public void Dispose() => Directory.Delete(_dataDir, recursive: true);

    private static string FixtureTasksJson => File.ReadAllText(Path.Combine(FindRepoRoot(), "tests", "TaskGuide.Storage.Tests", "fixtures", "data", "tasks.json"));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "task-guide.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not find repo root (task-guide.slnx) above " + AppContext.BaseDirectory);
    }

    private string SeedTasksJson(string json)
    {
        var path = Path.Combine(_dataDir, "tasks.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void The_whole_store_loads_into_typed_objects_at_startup()
    {
        SeedTasksJson(FixtureTasksJson);

        var store = new JsonStore(_dataDir);
        var tasks = store.Read().Tasks;

        Assert.Equal(5, tasks.Count);

        var shelfBracket = Assert.Single(tasks, t => t.Id == new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5FAV"));
        Assert.Equal("Fix the shelf bracket", shelfBracket.Title);
        Assert.Null(shelfBracket.Notes);
        Assert.Equal(new DateOnly(2026, 9, 1), shelfBracket.Deadline);
        Assert.Equal(new AbsoluteDefer(new DateOnly(2026, 8, 20)), shelfBracket.Defer);
        Assert.Equal(new DateTimeOffset(2026, 8, 15, 14, 2, 11, TimeSpan.Zero), shelfBracket.CreatedAt);
        Assert.Equal(["garge"], shelfBracket.Tags.LooseTags.Select(t => t.Value));
        Assert.Equal(["60"], shelfBracket.Tags.On(KnownDimensions.Duration).Select(v => v.Value));
        Assert.Equal(["garage"], shelfBracket.Tags.On(KnownDimensions.Location).Select(v => v.Value));

        var bins = Assert.Single(tasks, t => t.Id == new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5FB0"));
        Assert.Equal(new OffsetDefer(new BeforeOffset(1, OffsetUnit.Days)), bins.Defer);
        Assert.NotNull(bins.Recurrence);
        Assert.Equal(RecurrenceAnchor.Calendar, bins.Recurrence!.Anchor);
        var weeklyRule = Assert.IsType<EveryNWeeksOn>(bins.Recurrence.Rule);
        Assert.Equal(1, weeklyRule.N);
        Assert.Equal([DayOfWeek.Tuesday], weeklyRule.Weekdays);

        var kettle = Assert.Single(tasks, t => t.Id == new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5FB1"));
        Assert.Equal("White vinegar, an hour, rinse twice.", kettle.Notes);
        Assert.Equal(RecurrenceAnchor.Completion, kettle.Recurrence!.Anchor);
        Assert.Equal(new IntervalSinceCompletion(6, OffsetUnit.Months), kettle.Recurrence.Rule);
        Assert.Equal(new DateOnly(2026, 9, 1), kettle.Recurrence.FirstDue);

        var watch = Assert.Single(tasks, t => t.Id == new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5FB2"));
        Assert.Equal(new DateOnly(2026, 9, 10), watch.Postpone);
        Assert.Empty(watch.Tags.Dimensions);
    }

    [Fact]
    public void Every_read_is_served_from_memory_no_reread_of_disk()
    {
        var path = SeedTasksJson(FixtureTasksJson);
        var store = new JsonStore(_dataDir);
        var before = store.Read().Tasks.Count;

        File.WriteAllText(path, "[]");

        Assert.Equal(before, store.Read().Tasks.Count);
    }

    [Fact]
    public async Task A_mutation_writes_the_file_before_the_request_returns()
    {
        SeedTasksJson("[]");
        var store = new JsonStore(_dataDir);
        var newTask = NewTask("t_01ARZ3NDEKTSV4RRFFQ69G5NEW", "Water the plants");

        await store.MutateAsync(view => new StoreMutation([(IReadOnlyList<TaskItem>)[.. view.Tasks, newTask]]), CancellationToken.None);

        var onDisk = TaskCodec.Read(File.ReadAllText(Path.Combine(_dataDir, "tasks.json"))).Tasks;
        Assert.Single(onDisk);
        Assert.Equal("Water the plants", onDisk[0].Title);
        Assert.Single(store.Read().Tasks);
    }

    [Fact]
    public async Task A_write_is_atomic_a_killed_write_leaves_the_old_file_intact()
    {
        var originalJson = FixtureTasksJson;
        SeedTasksJson(originalJson);
        var store = new JsonStore(_dataDir);

        // An out-of-range enum value the codec cannot write — throws partway through
        // serialising the temp file, after some bytes are already on disk at the temp path,
        // before the destination is ever touched.
        var poisoned = new TaskItem(
            new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5BAD"),
            "Poisoned",
            null,
            TagSet.Empty,
            null,
            null,
            null,
            new Recurrence(RecurrenceAnchor.Completion, new IntervalSinceCompletion(1, (OffsetUnit)99), null),
            DateTimeOffset.UtcNow);

        await Assert.ThrowsAnyAsync<JsonException>(() =>
            store.MutateAsync(view => new StoreMutation([(IReadOnlyList<TaskItem>)[.. view.Tasks, poisoned]]), CancellationToken.None));

        var tasksPath = Path.Combine(_dataDir, "tasks.json");
        Assert.Equal(originalJson, File.ReadAllText(tasksPath));

        // no leftover temp file
        Assert.DoesNotContain(Directory.GetFiles(_dataDir), f => f.Contains(".tmp-"));

        // the in-memory view was never swapped either
        Assert.Equal(5, store.Read().Tasks.Count);
    }

    [Fact]
    public async Task One_global_write_lock_serialises_mutations()
    {
        SeedTasksJson("[]");
        var store = new JsonStore(_dataDir);

        var mutations = Enumerable.Range(0, 20)
            .Select(i => store.MutateAsync(
                view => new StoreMutation([(IReadOnlyList<TaskItem>)[.. view.Tasks, NewTask($"t_{i:D26}", $"Task {i}")]]),
                CancellationToken.None));

        await Task.WhenAll(mutations);

        Assert.Equal(20, store.Read().Tasks.Count);
        var onDisk = TaskCodec.Read(File.ReadAllText(Path.Combine(_dataDir, "tasks.json"))).Tasks;
        Assert.Equal(20, onDisk.Count);
        Assert.Equal(20, onDisk.Select(t => t.Id).Distinct().Count());
    }

    [Fact]
    public async Task A_read_never_blocks_on_a_write_and_never_sees_a_torn_view()
    {
        SeedTasksJson("[]");
        var store = new JsonStore(_dataDir);

        // `IStoreView` read never touches the write lock (see JsonStore.Read) — Read() has no
        // await point and no lock acquisition, so it cannot be blocked by a concurrent write by
        // construction. This drives a write large enough to take measurable wall-clock time and
        // confirms a concurrent Read() finishes well before it, and always sees a whole view
        // (never a task count that isn't the before- or after-count).
        var manyTasks = Enumerable.Range(0, 5_000)
            .Select(i => NewTask($"t_{i:D26}", new string('x', 500)))
            .ToArray();

        var mutate = store.MutateAsync(view => new StoreMutation([(IReadOnlyList<TaskItem>)manyTasks]), CancellationToken.None);
        var readDuringWrite = Task.Run(() => store.Read().Tasks.Count);

        var first = await Task.WhenAny(readDuringWrite, mutate);
        Assert.Same(readDuringWrite, first);

        var countDuringWrite = await readDuringWrite;
        Assert.True(countDuringWrite is 0 or 5_000, $"saw a torn view: {countDuringWrite} tasks");

        await mutate;
        Assert.Equal(5_000, store.Read().Tasks.Count);
    }

    [Fact]
    public async Task The_loaded_view_is_immutable_mutating_the_store_does_not_affect_an_earlier_read()
    {
        SeedTasksJson("[]");
        var store = new JsonStore(_dataDir);
        var before = store.Read().Tasks;
        Assert.Empty(before);

        await store.MutateAsync(view => new StoreMutation([(IReadOnlyList<TaskItem>)[.. view.Tasks, NewTask("t_01ARZ3NDEKTSV4RRFFQ69G5NEW", "New")]]), CancellationToken.None);

        Assert.Empty(before);
        Assert.Single(store.Read().Tasks);
    }

    [Fact]
    public async Task The_store_owns_its_storage_a_caller_mutating_its_own_list_afterwards_does_not_reach_the_store()
    {
        SeedTasksJson("[]");
        var store = new JsonStore(_dataDir);
        var callersList = new List<TaskItem> { NewTask("t_01ARZ3NDEKTSV4RRFFQ69G5NEW", "New") };

        await store.MutateAsync(_ => new StoreMutation([(IReadOnlyList<TaskItem>)callersList]), CancellationToken.None);
        Assert.Single(store.Read().Tasks);

        // The caller keeps its own reference and mutates it after handing it to the store.
        callersList.Add(NewTask("t_01ARZ3NDEKTSV4RRFFQ69G5OTH", "Should not appear"));
        callersList.Clear();

        Assert.Single(store.Read().Tasks); // the store's view must be unaffected either way
    }

    [Fact]
    public void Tasks_json_round_trips_with_no_status_field()
    {
        // TaskItem exposes no Status setter at all (#47) — there is no field to round-trip.
        Assert.DoesNotContain("\"status\"", FixtureTasksJson);

        var (tasks, extras) = TaskCodec.Read(FixtureTasksJson);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            TaskCodec.Write(writer, tasks, extras);
        }

        buffer.Position = 0;
        var roundTripped = JsonNode.Parse(buffer);
        var original = JsonNode.Parse(FixtureTasksJson);
        Assert.True(JsonNode.DeepEquals(original, roundTripped));
    }

    [Fact]
    public async Task An_unknown_field_written_by_a_newer_binary_survives_a_load_and_save_round_trip()
    {
        SeedTasksJson("""
            [
              { "id": "t_01ARZ3NDEKTSV4RRFFQ69G5FAV",
                "title": "From the future",
                "notes": null,
                "dimensions": {},
                "looseTags": [],
                "deadline": null,
                "defer": null,
                "postpone": null,
                "recurrence": null,
                "createdAt": "2026-08-15T14:02:11Z",
                "priority": "urgent" }
            ]
            """);

        var store = new JsonStore(_dataDir);

        // A no-op mutation: write the exact Tasks list back out unchanged.
        await store.MutateAsync(view => new StoreMutation([view.Tasks]), CancellationToken.None);

        var onDisk = JsonNode.Parse(File.ReadAllText(Path.Combine(_dataDir, "tasks.json")))!.AsArray();
        Assert.Equal("urgent", onDisk[0]!["priority"]!.GetValue<string>());
    }

    [Fact]
    public void AddJsonStore_loads_eagerly_a_corrupt_tasks_json_fails_at_registration_not_first_use()
    {
        SeedTasksJson("{ not valid json");
        var services = new ServiceCollection();

        // The whole store loads at startup per IStore's memory-authoritative contract — a bad
        // tasks.json must refuse to start (assert → snapshot → migrate → sweep → serve), not
        // surface as a failure on the first request that happens to touch the store.
        Assert.ThrowsAny<JsonException>(() => services.AddJsonStore(_dataDir));
    }

    private static TaskItem NewTask(string id, string title) =>
        new(new TaskId(id), title, null, TagSet.Empty, null, null, null, null, DateTimeOffset.UtcNow);
}
