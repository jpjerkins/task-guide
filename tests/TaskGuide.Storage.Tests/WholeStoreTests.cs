using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Infrastructure.Storage;
using Xunit;

namespace TaskGuide.Storage.Tests;

/// <summary>
/// Unit 2 of Task 7 (`tests/TEST-INVENTORY.md`, "Sequential · TaskGuide.Storage.Tests"): ordered,
/// multi-file writes. `MutateAsync` applies each write in <c>StoreMutation.OrderedWrites</c>
/// order, each atomic on its own, and swaps the read view only after the last one succeeds — a
/// write that throws part-way leaves the earlier files written, which is the accepted design
/// (see `IStore.MutateAsync`'s doc comment).
/// </summary>
public sealed class WholeStoreTests : IDisposable
{
    private readonly string _dataDir = Directory.CreateTempSubdirectory("taskguide-wholestore-tests-").FullName;

    public void Dispose() => Directory.Delete(_dataDir, recursive: true);

    private static string FixtureDataDir => Path.Combine(FindRepoRoot(), "tests", "TaskGuide.Storage.Tests", "fixtures", "data");

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "task-guide.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not find repo root (task-guide.slnx) above " + AppContext.BaseDirectory);
    }

    /// <summary>Copies the whole golden fixture directory (recursively) into the temp `_dataDir`.</summary>
    private void SeedWholeFixture() => CopyDirectory(FixtureDataDir, _dataDir);

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

    private static Event NewEvent(string id, DateOnly date, string name) =>
        new(new EventId(id), date, name, new TimeOnly(18, 0), new TimeOnly(19, 0), TagSet.Empty, null);

    private static DateOverride NewOverride(DateOnly date) =>
        new(date, [], null);

    private static DayTemplate NewDayTemplate(string id, string name) =>
        new(new DayTemplateId(id), name, [], []);

    /// <summary>
    /// Makes the file at <paramref name="path"/> impossible to write atomically, without
    /// disturbing any file that already exists elsewhere in its directory: <c>WriteAtomicAsync</c>
    /// writes a temp file, then renames it over <paramref name="path"/>, and a rename can never
    /// land a file on top of an existing directory (confirmed empirically for this runtime — see
    /// the report). Directory-permission denial (`chmod` on the containing directory, as
    /// `HealthReporterTests`/`TaskEndpointsTests` use) was tried first and rejected here because
    /// <paramref name="path"/> shares a directory with the write that must still succeed —
    /// denying the directory would block both. `path` must not already exist.
    /// </summary>
    private static void MakeUnwritable(string path)
    {
        Directory.CreateDirectory(path);
    }

    [Fact]
    public async Task An_Event_plus_Override_write_puts_the_Event_first()
    {
        // No pre-existing overrides.json — MakeUnwritable occupies that exact path with a
        // directory, so the file that would need to survive an aborted write does not exist to
        // begin with.
        var eventsPath = Path.Combine(_dataDir, "events.json");
        var overridesPath = Path.Combine(_dataDir, "overrides.json");
        MakeUnwritable(overridesPath);

        var store = new JsonStore(_dataDir);
        var newEvent = NewEvent("evt_01ARZ3NDEKTSV4RRFFQ69G5N00", new DateOnly(2026, 9, 5), "Band concert");
        var newOverride = NewOverride(new DateOnly(2026, 9, 5));

        await Assert.ThrowsAnyAsync<Exception>(() => store.MutateAsync(
            _ => new StoreMutation([new EventsWrite([newEvent]), new OverridesWrite([newOverride])]),
            CancellationToken.None));

        // The Event write ran (and completed) before the Override write was even attempted —
        // if OrderedWrites were applied in reverse, the Override write would have thrown first
        // and events.json would never have been touched.
        Assert.True(File.Exists(eventsPath));
        var (onDiskEvents, _) = EventCodec.Read(File.ReadAllText(eventsPath));
        Assert.Contains(onDiskEvents, e => e.Id == newEvent.Id);
    }

    [Fact]
    public async Task A_crash_between_the_two_leaves_the_state_the_overlap_check_detects_and_the_next_read_re_offers_the_prompt()
    {
        if (OperatingSystem.IsWindows()) return; // directory-collision write denial is exercised for POSIX (the deployment target); see MakeUnwritable.

        var eventsPath = Path.Combine(_dataDir, "events.json");
        var overridesPath = Path.Combine(_dataDir, "overrides.json");
        MakeUnwritable(overridesPath);

        var store = new JsonStore(_dataDir);
        var newEvent = NewEvent("evt_01ARZ3NDEKTSV4RRFFQ69G5N01", new DateOnly(2026, 9, 6), "Dentist");
        var newOverride = NewOverride(new DateOnly(2026, 9, 6));

        await Assert.ThrowsAnyAsync<Exception>(() => store.MutateAsync(
            _ => new StoreMutation([new EventsWrite([newEvent]), new OverridesWrite([newOverride])]),
            CancellationToken.None));

        // The Event survived; the Override that was supposed to land alongside it did not — this
        // is exactly the inconsistency the overlap check is designed to notice and re-prompt for.
        var (onDiskEvents, _) = EventCodec.Read(File.ReadAllText(eventsPath));
        Assert.Contains(onDiskEvents, e => e.Id == newEvent.Id);

        Assert.False(store.LastWriteSucceeded);
        Assert.DoesNotContain(store.Read().Events, e => e.Id == newEvent.Id); // the view was never swapped

        // A fresh read (a new process starting up against the same directory) sees the same
        // half-applied state — the Event is there, no Override answers it.
        Directory.Delete(overridesPath); // undo MakeUnwritable's directory so Load() can run
        var reloaded = new JsonStore(_dataDir);
        Assert.Contains(reloaded.Read().Events, e => e.Id == newEvent.Id);
        Assert.DoesNotContain(reloaded.Read().Overrides, o => o.Date == newOverride.Date);
    }

    [Fact]
    public async Task A_mutation_writes_every_affected_file_before_the_request_returns_not_only_the_first()
    {
        SeedWholeFixture();
        var store = new JsonStore(_dataDir);
        var view = store.Read();

        var newTemplates = (IReadOnlyList<DayTemplate>)[.. view.DayTemplates, NewDayTemplate("dt_01ARZ3NDEKTSV4RRFFQ69G5N02", "New template")];
        var newOverrides = (IReadOnlyList<DateOverride>)[.. view.Overrides, NewOverride(new DateOnly(2026, 9, 7))];

        await store.MutateAsync(
            _ => new StoreMutation([new DayTemplatesWrite(newTemplates), new OverridesWrite(newOverrides)]),
            CancellationToken.None);

        var (onDiskTemplates, _) = DayTemplateCodec.Read(File.ReadAllText(Path.Combine(_dataDir, "day-templates.json")));
        var (onDiskOverrides, _) = OverrideCodec.Read(File.ReadAllText(Path.Combine(_dataDir, "overrides.json")));

        Assert.Equal(newTemplates.Count, onDiskTemplates.Count);
        Assert.Contains(onDiskTemplates, t => t.Name == "New template");
        Assert.Equal(newOverrides.Count, onDiskOverrides.Count);
        Assert.Contains(onDiskOverrides, o => o.Date == new DateOnly(2026, 9, 7));
    }

    [Fact]
    public async Task A_partially_failed_multi_file_write_leaves_LastWriteSucceeded_false_and_does_not_swap_the_view()
    {
        SeedWholeFixture();
        var patternsPath = Path.Combine(_dataDir, "patterns.json");
        File.Delete(patternsPath);
        MakeUnwritable(patternsPath); // the second write's target — the first (Tasks) stays writable

        var store = new JsonStore(_dataDir);
        var beforeTaskCount = store.Read().Tasks.Count;
        var newTasks = (IReadOnlyList<TaskGuide.Domain.Tasks.TaskItem>)[
            .. store.Read().Tasks,
            new TaskGuide.Domain.Tasks.TaskItem(new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5P00"), "New", null, TagSet.Empty, null, null, null, null, DateTimeOffset.UtcNow)];

        await Assert.ThrowsAnyAsync<Exception>(() => store.MutateAsync(
            _ => new StoreMutation([new TasksWrite(newTasks), new PatternsWrite(store.Read().Patterns)]),
            CancellationToken.None));

        Assert.False(store.LastWriteSucceeded);
        Assert.Equal(beforeTaskCount, store.Read().Tasks.Count); // the view was not swapped, even though tasks.json itself did get rewritten on disk

        var (onDiskTasks, _) = TaskCodec.Read(File.ReadAllText(Path.Combine(_dataDir, "tasks.json")));
        Assert.Equal(beforeTaskCount + 1, onDiskTasks.Count); // the first write's file was in fact rewritten — the accepted, non-rolled-back design
    }

    [Fact]
    public async Task A_write_of_one_collection_leaves_every_other_collection_in_the_swapped_in_view_unchanged()
    {
        SeedWholeFixture();
        var store = new JsonStore(_dataDir);
        var before = store.Read();

        var beforeDayTemplates = before.DayTemplates;
        var beforePatterns = before.Patterns;
        var beforeOverrides = before.Overrides;
        var beforeEvents = before.Events;
        var beforeEventExceptions = before.EventExceptions;
        var beforeDerivedCompletions = before.DerivedCompletions;
        var beforeCompletions = before.CompletionsFor(new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5FB0"));
        var beforeFires = before.FiresOn(new DateOnly(2026, 8, 15));

        await store.MutateAsync(
            view => new StoreMutation([new TasksWrite((IReadOnlyList<TaskGuide.Domain.Tasks.TaskItem>)[
                .. view.Tasks,
                new TaskGuide.Domain.Tasks.TaskItem(new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5P01"), "Another", null, TagSet.Empty, null, null, null, null, DateTimeOffset.UtcNow)])]),
            CancellationToken.None);

        var after = store.Read();

        Assert.Equal(beforeDayTemplates.Count, after.DayTemplates.Count);
        Assert.NotEmpty(after.DayTemplates);
        Assert.Equal(beforePatterns.Patterns.Count, after.Patterns.Patterns.Count);
        Assert.NotEmpty(after.Patterns.Patterns);
        Assert.Equal(beforeOverrides.Count, after.Overrides.Count);
        Assert.NotEmpty(after.Overrides);
        Assert.Equal(beforeEvents.Count, after.Events.Count);
        Assert.NotEmpty(after.Events);
        Assert.Equal(beforeEventExceptions.Count, after.EventExceptions.Count);
        Assert.NotEmpty(after.EventExceptions);
        Assert.Equal(beforeDerivedCompletions.Count, after.DerivedCompletions.Count);
        Assert.NotEmpty(after.DerivedCompletions);
        Assert.Equal(beforeCompletions.Entries.Count, after.CompletionsFor(new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5FB0")).Entries.Count);
        Assert.NotEmpty(after.CompletionsFor(new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5FB0")).Entries);
        Assert.Equal(beforeFires.Rows.Count, after.FiresOn(new DateOnly(2026, 8, 15)).Rows.Count);
        Assert.NotEmpty(after.FiresOn(new DateOnly(2026, 8, 15)).Rows);
    }
}
