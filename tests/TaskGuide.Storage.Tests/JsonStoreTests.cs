using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using OneOf;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.Infrastructure.Storage;
using Xunit;

namespace TaskGuide.Storage.Tests;

/// <summary>
/// Against `fixtures/data`, the golden store (`tests/TEST-INVENTORY.md`'s "Sequential ·
/// TaskGuide.Storage.Tests" section). The whole store now loads (every collection in the golden
/// store, not only `tasks.json`) and most tests here still exercise the Tasks slice, since
/// writing is still Tasks-only for the walking skeleton (#51) — `MutateAsync` accepts only a
/// Tasks write and throws for anything else.
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

    /// <summary>Copies the whole golden fixture directory (recursively) into the temp `_dataDir`.</summary>
    private void SeedWholeFixture()
    {
        var fixtureDir = Path.Combine(FindRepoRoot(), "tests", "TaskGuide.Storage.Tests", "fixtures", "data");
        CopyDirectory(fixtureDir, _dataDir);
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var filePath in Directory.EnumerateFiles(sourceDir))
        {
            File.Copy(filePath, Path.Combine(destinationDir, Path.GetFileName(filePath)), overwrite: true);
        }

        foreach (var subDir in Directory.EnumerateDirectories(sourceDir))
        {
            CopyDirectory(subDir, Path.Combine(destinationDir, Path.GetFileName(subDir)));
        }
    }

    [Fact]
    public void The_whole_store_loads_into_typed_objects_at_startup()
    {
        SeedWholeFixture();

        var store = new JsonStore(_dataDir);
        var view = store.Read();
        var tasks = view.Tasks;

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

        Assert.Equal(3, view.DayTemplates.Count);
        var volleyballTuesday = Assert.Single(view.DayTemplates, t => t.Id == new DayTemplateId("dt_01ARZ3NDEKTSV4RRFFQ69G5G01"));
        Assert.Equal("Volleyball Tuesday", volleyballTuesday.Name);
        var karate = Assert.Single(volleyballTuesday.EventPrototypes);
        Assert.Equal("Karate", karate.Name);

        Assert.Equal(new PatternId("p_01ARZ3NDEKTSV4RRFFQ69G5K00"), view.Patterns.ActivePatternId);
        Assert.Equal(2, view.Patterns.Patterns.Count);

        Assert.Equal(3, view.Overrides.Count);
        var volleyballOverride = Assert.Single(view.Overrides, o => o.Date == new DateOnly(2026, 8, 15));
        Assert.Equal("Volleyball Tuesday", volleyballOverride.Used!.TemplateName);

        Assert.Equal(2, view.Events.Count);
        var bandConcert = Assert.Single(view.Events, e => e.Id == new EventId("evt_01ARZ3NDEKTSV4RRFFQ69G5M00"));
        Assert.Equal("Band concert", bandConcert.Name);

        Assert.Equal(2, view.EventExceptions.Count);
        var karateException = Assert.Single(view.EventExceptions, e => e.Date == new DateOnly(2026, 8, 25));
        Assert.Equal("Karate (late)", karateException.Name);

        var derived = Assert.Single(view.DerivedCompletions);
        Assert.Equal(new RuleId("absence"), derived.RuleId);
        Assert.Equal(new DateOnly(2026, 9, 27), derived.Due);

        var completions = view.CompletionsFor(new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5FB0"));
        Assert.Equal(2, completions.Entries.Count);
        Assert.Equal(new DateOnly(2026, 8, 11), completions.Entries[0].Due);

        var fires = view.FiresOn(new DateOnly(2026, 8, 15));
        Assert.Equal(3, fires.Rows.Count);
        Assert.Contains(fires.Rows, r => r.Kind == FireKind.Fallback);
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

        await store.MutateAsync<Never>(view => new StoreMutation([new TasksWrite((IReadOnlyList<TaskItem>)[.. view.Tasks, newTask])]), CancellationToken.None);

        var onDisk = TaskCodec.Read(File.ReadAllText(Path.Combine(_dataDir, "tasks.json")));
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
            store.MutateAsync<Never>(view => new StoreMutation([new TasksWrite((IReadOnlyList<TaskItem>)[.. view.Tasks, poisoned])]), CancellationToken.None));

        var tasksPath = Path.Combine(_dataDir, "tasks.json");
        Assert.Equal(originalJson, File.ReadAllText(tasksPath));

        // no leftover temp file
        Assert.DoesNotContain(Directory.GetFiles(_dataDir), f => f.Contains(".tmp-"));

        // the in-memory view was never swapped either
        Assert.Equal(5, store.Read().Tasks.Count);
    }

    /// <summary>A refusal type a test can actually construct — <see cref="Never"/> is uninhabited by design.</summary>
    private sealed record Refused(string Why);

    [Fact]
    public async Task A_refusal_inside_the_write_lock_writes_nothing_and_leaves_the_lock_usable()
    {
        var originalJson = FixtureTasksJson;
        SeedTasksJson(originalJson);
        var store = new JsonStore(_dataDir);

        var result = await store.MutateAsync<Refused>(
            _ => OneOf<StoreMutation, Refused>.FromT1(new Refused("stale view")),
            CancellationToken.None);

        Assert.True(result.TryPickT1(out var refusal, out _));
        Assert.Equal("stale view", refusal.Why);

        var tasksPath = Path.Combine(_dataDir, "tasks.json");
        Assert.Equal(originalJson, File.ReadAllText(tasksPath));
        Assert.Equal(5, store.Read().Tasks.Count);

        // A refusal attempts no write, so LastWriteSucceeded — observed, not probed — stays null:
        // an unattempted write is not evidence of anything.
        Assert.Null(store.LastWriteSucceeded);

        // The write lock was released, not left held by a `return` that skipped the `finally` —
        // an ordinary mutation right after the refusal must still land.
        await store.MutateAsync<Never>(view => new StoreMutation([new TasksWrite((IReadOnlyList<TaskItem>)[.. view.Tasks, NewTask("t_01ARZ3NDEKTSV4RRFFQ69G5NEW", "After refusal")])]), CancellationToken.None);
        Assert.Equal(6, store.Read().Tasks.Count);
    }

    [Fact]
    public async Task An_applied_mutation_returns_Applied()
    {
        SeedTasksJson("[]");
        var store = new JsonStore(_dataDir);

        var result = await store.MutateAsync<Never>(
            view => new StoreMutation([new TasksWrite((IReadOnlyList<TaskItem>)[.. view.Tasks, NewTask("t_01ARZ3NDEKTSV4RRFFQ69G5NEW", "Water the plants")])]),
            CancellationToken.None);

        Assert.True(result.TryPickT0(out _, out _));
    }

    [Fact]
    public async Task One_global_write_lock_serialises_mutations()
    {
        SeedTasksJson("[]");
        var store = new JsonStore(_dataDir);

        var mutations = Enumerable.Range(0, 20)
            .Select(i => store.MutateAsync<Never>(
                view => new StoreMutation([new TasksWrite((IReadOnlyList<TaskItem>)[.. view.Tasks, NewTask($"t_{i:D26}", $"Task {i}")])]),
                CancellationToken.None));

        await Task.WhenAll(mutations);

        Assert.Equal(20, store.Read().Tasks.Count);
        var onDisk = TaskCodec.Read(File.ReadAllText(Path.Combine(_dataDir, "tasks.json")));
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

        var mutate = store.MutateAsync<Never>(view => new StoreMutation([new TasksWrite((IReadOnlyList<TaskItem>)manyTasks)]), CancellationToken.None);
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

        await store.MutateAsync<Never>(view => new StoreMutation([new TasksWrite((IReadOnlyList<TaskItem>)[.. view.Tasks, NewTask("t_01ARZ3NDEKTSV4RRFFQ69G5NEW", "New")])]), CancellationToken.None);

        Assert.Empty(before);
        Assert.Single(store.Read().Tasks);
    }

    [Fact]
    public async Task The_store_owns_its_storage_a_caller_mutating_its_own_list_afterwards_does_not_reach_the_store()
    {
        SeedTasksJson("[]");
        var store = new JsonStore(_dataDir);
        var callersList = new List<TaskItem> { NewTask("t_01ARZ3NDEKTSV4RRFFQ69G5NEW", "New") };

        await store.MutateAsync<Never>(_ => new StoreMutation([new TasksWrite((IReadOnlyList<TaskItem>)callersList)]), CancellationToken.None);
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

        var tasks = TaskCodec.Read(FixtureTasksJson);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            TaskCodec.Write(writer, tasks);
        }

        buffer.Position = 0;
        var roundTripped = JsonNode.Parse(buffer);
        var original = JsonNode.Parse(FixtureTasksJson);
        Assert.True(JsonNode.DeepEquals(original, roundTripped));
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

    [Fact]
    public void A_missing_collection_file_loads_as_empty_rather_than_throwing_a_fresh_data_is_valid()
    {
        // No files seeded at all — a brand-new /data, before anything has ever been written.
        var store = new JsonStore(_dataDir);
        var view = store.Read();

        Assert.Empty(view.Tasks);
        Assert.Empty(view.DayTemplates);
        Assert.Empty(view.Patterns.Patterns);
        Assert.Empty(view.Overrides);
        Assert.Empty(view.Events);
        Assert.Empty(view.EventExceptions);
        Assert.Empty(view.DerivedCompletions);
        Assert.Equal(CompletionLog.Empty(new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5FAV")), view.CompletionsFor(new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5FAV")));
        Assert.Equal(new DayFires(new DateOnly(2026, 8, 15), []), view.FiresOn(new DateOnly(2026, 8, 15)));
    }

    [Fact]
    public void AddJsonStore_loads_eagerly_a_corrupt_day_templates_json_fails_at_registration_not_first_use()
    {
        File.WriteAllText(Path.Combine(_dataDir, "day-templates.json"), "{ not valid json");
        var services = new ServiceCollection();

        // Extends the tasks.json-only rule above to every collection: a bad day-templates.json
        // must refuse to start too, not surface on the first request that happens to touch it.
        Assert.ThrowsAny<JsonException>(() => services.AddJsonStore(_dataDir));
    }

    private static TaskItem NewTask(string id, string title) =>
        new(new TaskId(id), title, null, TagSet.Empty, null, null, null, null, DateTimeOffset.UtcNow);
}
