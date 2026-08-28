using System.Text.Json;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.Infrastructure.Storage;
using Xunit;

namespace TaskGuide.Storage.Tests;

/// <summary>
/// Task 8b: <see cref="StartupSequence"/>, composing 8a's <see cref="ManifestCodec"/> and
/// <see cref="SnapshotWriter"/> with the Dimension registry assert and <see cref="RegistrySweep"/>.
/// Against `tests/TEST-INVENTORY.md`'s "Sequential · TaskGuide.Storage.Tests" section.
/// </summary>
public sealed class StartupSequenceTests : IDisposable
{
    private readonly string _dataDir = Directory.CreateTempSubdirectory("taskguide-startup-tests-").FullName;
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 28, 10, 0, 0, TimeSpan.Zero);

    public void Dispose() => Directory.Delete(_dataDir, recursive: true);

    private static DimensionRegistry SingleDimensionRegistry() =>
        new([new CategoricalDimension(new DimensionId("loc"), "Location", [new TagValue("garage")])]);

    private static DimensionRegistry CollidingRegistry() =>
        new([
            new CategoricalDimension(new DimensionId("loc"), "Location", [new TagValue("garage")]),
            new CategoricalDimension(new DimensionId("place"), "Place", [new TagValue("garage")]),
        ]);

    private void WriteManifest(int version) =>
        File.WriteAllText(Path.Combine(_dataDir, "manifest.json"), $"{{ \"version\": {version} }}");

    private string ManifestPath => Path.Combine(_dataDir, "manifest.json");

    private string SnapshotsDir => Path.Combine(_dataDir, "snapshots");

    private StartupSequence NewSequence(
        DimensionRegistry? registry = null,
        IReadOnlyList<StoreMigration>? migrations = null,
        Func<string, CancellationToken, Task>? signal = null,
        JsonStore? store = null) =>
        new(
            store ?? new JsonStore(_dataDir),
            _dataDir,
            registry ?? SingleDimensionRegistry(),
            new SnapshotWriter(_dataDir),
            migrations ?? [],
            () => FixedNow,
            signal ?? ((_, _) => Task.CompletedTask));

    /// <summary>Writes `tasks.json` directly, bypassing <see cref="JsonStore.MutateAsync"/> — so a
    /// <see cref="JsonStore"/> constructed over it starts with <see cref="IStore.LastWriteSucceeded"/>
    /// still <c>null</c>, letting a test observe whether a later call issues the store's first
    /// write, uncontaminated by the seed itself.</summary>
    private void SeedTasksFileRaw(IReadOnlyList<TaskItem> tasks)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            TaskCodec.Write(writer, tasks, new Dictionary<TaskId, IReadOnlyList<KeyValuePair<string, JsonElement>>>());
        }

        File.WriteAllBytes(Path.Combine(_dataDir, "tasks.json"), buffer.ToArray());
    }

    [Fact]
    public async Task Manifest_json_version_mismatch_runs_the_ordered_N_to_N_plus_1_steps_at_startup()
    {
        // -1 → 0 → 1: two ordered steps landing exactly on CurrentVersion (1), not past it —
        // ManifestCodec.CurrentVersion is a real const this binary enforces (see the version-ahead
        // and overshoot tests below), so a multi-step walk test must stay within it rather than
        // faking the constant itself.
        WriteManifest(-1);
        var order = new List<int>();
        IReadOnlyList<StoreMigration> migrations =
        [
            new StoreMigration(-1, 0, (_, _) => { order.Add(-1); return Task.CompletedTask; }),
            new StoreMigration(0, 1, (_, _) => { order.Add(0); return Task.CompletedTask; }),
        ];
        var sut = NewSequence(migrations: migrations);

        await sut.MigrateAsync(CancellationToken.None);

        Assert.Equal([-1, 0], order);
        Assert.Equal(1, ManifestCodec.Read(File.ReadAllText(ManifestPath)));
    }

    [Fact]
    public async Task A_snapshot_is_written_once_per_startup_and_only_when_that_startup_will_write()
    {
        // Nothing pending: no manifest mismatch, no fake migrations, registry has nothing to sweep.
        WriteManifest(1);
        var quiet = NewSequence();
        await quiet.RunAsync(CancellationToken.None);
        Assert.False(Directory.Exists(SnapshotsDir));

        // A pending migration exists (0 → 1, landing exactly on CurrentVersion): the startup will
        // write, so exactly one snapshot is taken.
        Directory.Delete(_dataDir, recursive: true);
        Directory.CreateDirectory(_dataDir);
        WriteManifest(0);
        IReadOnlyList<StoreMigration> migrations = [new StoreMigration(0, 1, (_, _) => Task.CompletedTask)];
        var busy = NewSequence(migrations: migrations);

        await busy.RunAsync(CancellationToken.None);

        Assert.True(Directory.Exists(SnapshotsDir));
        Assert.Single(Directory.GetDirectories(SnapshotsDir));
    }

    [Fact]
    public async Task A_registry_collision_signals_outbound_before_exiting()
    {
        WriteManifest(1);
        var signalled = new List<string>();
        var sut = NewSequence(
            registry: CollidingRegistry(),
            signal: (message, _) => { signalled.Add(message); return Task.CompletedTask; });

        await Assert.ThrowsAsync<DuplicateDimensionValueException>(() => sut.RunAsync(CancellationToken.None));

        var signalledMessage = Assert.Single(signalled);
        Assert.Contains("garage", signalledMessage);
        Assert.False(Directory.Exists(SnapshotsDir));
    }

    [Fact]
    public async Task A_store_already_at_CurrentVersion_runs_no_migration_step_and_takes_no_snapshot()
    {
        WriteManifest(ManifestCodec.CurrentVersion);
        var ran = false;
        // A step registered for a different version must not fire just because it exists —
        // pins that the skip is keyed on the store's own version, not on an empty list.
        IReadOnlyList<StoreMigration> migrations = [new StoreMigration(ManifestCodec.CurrentVersion - 1, ManifestCodec.CurrentVersion, (_, _) => { ran = true; return Task.CompletedTask; })];
        var sut = NewSequence(migrations: migrations);

        await sut.RunAsync(CancellationToken.None);

        Assert.False(ran);
        Assert.False(Directory.Exists(SnapshotsDir));
        Assert.Equal(ManifestCodec.CurrentVersion, ManifestCodec.Read(File.ReadAllText(ManifestPath)));
    }

    [Fact]
    public async Task A_version_ahead_of_this_binary_refuses_to_start_named()
    {
        WriteManifest(ManifestCodec.CurrentVersion + 1);
        var sut = NewSequence();

        var ex = await Assert.ThrowsAsync<StoreVersionAheadException>(() => sut.RunAsync(CancellationToken.None));

        Assert.Equal(ManifestCodec.CurrentVersion + 1, ex.StoredVersion);
        Assert.Equal(ManifestCodec.CurrentVersion, ex.CurrentVersion);
        Assert.Contains((ManifestCodec.CurrentVersion + 1).ToString(), ex.Message);
        Assert.False(Directory.Exists(SnapshotsDir));
    }

    [Fact]
    public async Task Manifest_json_is_written_only_after_every_migration_step_succeeds()
    {
        // -1 → 0 → 1 (throws): stays within CurrentVersion so the failure under test is the
        // step's own exception, not the overshoot guard below firing first.
        WriteManifest(-1);
        var firstStepRan = false;
        IReadOnlyList<StoreMigration> migrations =
        [
            new StoreMigration(-1, 0, (_, _) => { firstStepRan = true; return Task.CompletedTask; }),
            new StoreMigration(0, 1, (_, _) => throw new InvalidOperationException("step 2 blew up")),
        ];
        var sut = NewSequence(migrations: migrations);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.MigrateAsync(CancellationToken.None));

        Assert.True(firstStepRan);
        Assert.Equal(-1, ManifestCodec.Read(File.ReadAllText(ManifestPath)));
    }

    /// <summary>
    /// I2 (review): a cycle in the migration list must refuse promptly rather than spin the walk
    /// forever — an infinite hang at startup instead of an exception is strictly worse than either.
    /// </summary>
    [Fact]
    public async Task A_migration_cycle_refuses_to_start_instead_of_hanging()
    {
        WriteManifest(1);
        IReadOnlyList<StoreMigration> migrations =
        [
            new StoreMigration(1, 2, (_, _) => Task.CompletedTask),
            new StoreMigration(2, 1, (_, _) => Task.CompletedTask),
        ];
        var sut = NewSequence(migrations: migrations);

        // If the monotonicity guard is missing this call does not return at all — Assert.ThrowsAsync
        // still awaits it directly (no timeout), which is an accepted risk here: the guard being
        // tested for is exactly what stands between this and a genuine hang.
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.MigrateAsync(CancellationToken.None));
    }

    /// <summary>
    /// I2 (review): a walk that would land past `CurrentVersion` must refuse rather than write a
    /// `manifest.json` this very binary would then refuse to start on its next boot.
    /// </summary>
    [Fact]
    public async Task A_migration_walk_that_would_overshoot_CurrentVersion_refuses_to_start()
    {
        WriteManifest(ManifestCodec.CurrentVersion);
        IReadOnlyList<StoreMigration> migrations =
        [
            new StoreMigration(ManifestCodec.CurrentVersion, ManifestCodec.CurrentVersion + 1, (_, _) => Task.CompletedTask),
        ];
        var sut = NewSequence(migrations: migrations);

        var ex = await Assert.ThrowsAsync<StoreVersionAheadException>(() => sut.MigrateAsync(CancellationToken.None));

        Assert.Equal(ManifestCodec.CurrentVersion + 1, ex.StoredVersion);
        Assert.Equal(ManifestCodec.CurrentVersion, ManifestCodec.Read(File.ReadAllText(ManifestPath)));
    }

    /// <summary>
    /// I4 (review) + M-c: a fresh `/data` — no `manifest.json` at all — establishes one at
    /// `CurrentVersion` without a snapshot, and `SnapshotAsync` itself tolerates "nothing exists
    /// yet to protect" rather than handing an empty list to `SnapshotWriter.TakeAsync`.
    /// </summary>
    [Fact]
    public async Task Startup_against_a_fresh_data_dir_creates_manifest_json_without_snapshotting()
    {
        var sut = NewSequence();

        // Calling SnapshotAsync directly, before anything else exists, exercises its own
        // "nothing to protect" guard in isolation — RunAsync below never reaches this call at all
        // for a fresh store, since willWrite is false when there is nothing pending and nothing
        // to sweep.
        await sut.SnapshotAsync(CancellationToken.None);
        Assert.False(Directory.Exists(SnapshotsDir));

        await sut.RunAsync(CancellationToken.None);

        Assert.True(File.Exists(ManifestPath));
        Assert.Equal(ManifestCodec.CurrentVersion, ManifestCodec.Read(File.ReadAllText(ManifestPath)));
        Assert.False(Directory.Exists(SnapshotsDir));
    }

    /// <summary>M-a: the "writes nothing when nothing moved" guarantee, observed at the
    /// <see cref="IStore"/> level rather than by inspecting <see cref="StartupSequence"/>'s
    /// internals — <see cref="IStore.LastWriteSucceeded"/> stays <c>null</c> (no write ever
    /// attempted) rather than merely "no visible change", which is what distinguishes "no
    /// MutateAsync call was made" from "a MutateAsync call was made with nothing to do".</summary>
    [Fact]
    public async Task The_registry_sweep_makes_no_MutateAsync_call_when_nothing_moved()
    {
        var alreadyCorrect = new TaskItem(
            new TaskId("t_test0000000000000000002"),
            "Nothing to sweep",
            null,
            new TagSet(new Dictionary<DimensionId, IReadOnlyList<TagValue>> { [new DimensionId("loc")] = [new TagValue("garage")] }, []),
            null, null, null, null,
            new DateTimeOffset(2026, 8, 28, 9, 0, 0, TimeSpan.Zero));
        SeedTasksFileRaw([alreadyCorrect]);

        var store = new JsonStore(_dataDir);
        Assert.Null(store.LastWriteSucceeded);

        var sut = NewSequence(store: store);
        await sut.SweepRegistryAsync(CancellationToken.None);

        Assert.Null(store.LastWriteSucceeded);
    }

    /// <summary>
    /// Beyond the six pinned behaviours — added with its own inventory line per the brief's rule
    /// ("any test beyond an inventory line gets a new inventory line appended in the same
    /// commit"). Exercises <see cref="StartupSequence.SweepRegistryAsync"/> itself: without this,
    /// the promote/demote wiring that <see cref="IStartupSequence.SweepRegistryAsync"/> exists to
    /// provide would ship with no test touching it at all.
    /// </summary>
    /// <remarks>
    /// Asserts against a freshly-constructed <see cref="JsonStore"/> reloaded from
    /// <c>tasks.json</c> — not the original <see cref="JsonStore"/> instance's in-memory view —
    /// which is what actually proves the change reached disk rather than only the in-process
    /// view (the review's M-f). Also asserts `day-templates.json` was never created: nothing in
    /// this store has a Day template, so a write to that collection (I3's sibling concern, M14 in
    /// the review) would be a spurious write nothing here should trigger.
    /// </remarks>
    [Fact]
    public async Task The_registry_sweep_promotes_a_loose_Tag_the_registry_now_claims_and_writes_the_change()
    {
        var store = new JsonStore(_dataDir);
        var looseTagged = new TaskItem(
            new TaskId("t_test0000000000000000001"),
            "Sweep me",
            null,
            new TagSet(new Dictionary<DimensionId, IReadOnlyList<TagValue>>(), [new LooseTag("garage")]),
            null, null, null, null,
            DateTimeOffset.UtcNow);
        await store.MutateAsync(_ => new StoreMutation([new TasksWrite([looseTagged])]), CancellationToken.None);

        var sut = NewSequence(store: store);
        await sut.SweepRegistryAsync(CancellationToken.None);

        var reloaded = new JsonStore(_dataDir);
        var swept = Assert.Single(reloaded.Read().Tasks);
        Assert.Empty(swept.Tags.LooseTags);
        Assert.Equal(new TagValue("garage"), Assert.Single(swept.Tags.On(new DimensionId("loc"))));
        Assert.False(File.Exists(Path.Combine(_dataDir, "day-templates.json")));
    }

    /// <summary>
    /// I3 (review): three of the four swept collections (Day templates, Overrides, Events)
    /// shipped with zero coverage — disabling all three left every other test green. This pins
    /// Day template Window Tags specifically; Overrides and Events share the same
    /// <see cref="RegistrySweep.Sweep"/> call and the same <c>TagSetsEqual</c> change-detection,
    /// so one non-Task collection closes the coverage gap the review demonstrated.
    /// </summary>
    [Fact]
    public async Task The_registry_sweep_promotes_a_loose_Tag_on_a_Day_template_Window()
    {
        var store = new JsonStore(_dataDir);
        var looseTaggedWindow = new AvailabilityWindow(
            new WindowId("w_test000000000000000001"),
            "Evening",
            new TimeOnly(18, 0),
            new TimeOnly(21, 0),
            new TagSet(new Dictionary<DimensionId, IReadOnlyList<TagValue>>(), [new LooseTag("garage")]));
        var template = new DayTemplate(new DayTemplateId("dt_test00000000000000001"), "Weekday", [looseTaggedWindow], []);
        await store.MutateAsync(_ => new StoreMutation([new DayTemplatesWrite([template])]), CancellationToken.None);

        var sut = NewSequence(store: store);
        await sut.SweepRegistryAsync(CancellationToken.None);

        var reloaded = new JsonStore(_dataDir);
        var sweptTemplate = Assert.Single(reloaded.Read().DayTemplates);
        var sweptWindow = Assert.Single(sweptTemplate.Windows);
        Assert.Empty(sweptWindow.Tags.LooseTags);
        Assert.Equal(new TagValue("garage"), Assert.Single(sweptWindow.Tags.On(new DimensionId("loc"))));
    }
}
