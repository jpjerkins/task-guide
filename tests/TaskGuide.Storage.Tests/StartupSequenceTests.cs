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

    [Fact]
    public async Task Manifest_json_version_mismatch_runs_the_ordered_N_to_N_plus_1_steps_at_startup()
    {
        WriteManifest(1);
        var order = new List<int>();
        IReadOnlyList<StoreMigration> migrations =
        [
            new StoreMigration(1, 2, (_, _) => { order.Add(1); return Task.CompletedTask; }),
            new StoreMigration(2, 3, (_, _) => { order.Add(2); return Task.CompletedTask; }),
        ];
        var sut = NewSequence(migrations: migrations);

        await sut.MigrateAsync(CancellationToken.None);

        Assert.Equal([1, 2], order);
        Assert.Equal(3, ManifestCodec.Read(File.ReadAllText(Path.Combine(_dataDir, "manifest.json"))));
    }

    [Fact]
    public async Task A_snapshot_is_written_once_per_startup_and_only_when_that_startup_will_write()
    {
        // Nothing pending: no manifest mismatch, no fake migrations, registry has nothing to sweep.
        WriteManifest(1);
        var quiet = NewSequence();
        await quiet.RunAsync(CancellationToken.None);
        Assert.False(Directory.Exists(SnapshotsDir));

        // A pending migration exists: the startup will write, so exactly one snapshot is taken.
        Directory.Delete(_dataDir, recursive: true);
        Directory.CreateDirectory(_dataDir);
        WriteManifest(1);
        IReadOnlyList<StoreMigration> migrations = [new StoreMigration(1, 2, (_, _) => Task.CompletedTask)];
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
        IReadOnlyList<StoreMigration> migrations = [new StoreMigration(0, 1, (_, _) => { ran = true; return Task.CompletedTask; })];
        var sut = NewSequence(migrations: migrations);

        await sut.RunAsync(CancellationToken.None);

        Assert.False(ran);
        Assert.False(Directory.Exists(SnapshotsDir));
        Assert.Equal(ManifestCodec.CurrentVersion, ManifestCodec.Read(File.ReadAllText(Path.Combine(_dataDir, "manifest.json"))));
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
        WriteManifest(1);
        var firstStepRan = false;
        IReadOnlyList<StoreMigration> migrations =
        [
            new StoreMigration(1, 2, (_, _) => { firstStepRan = true; return Task.CompletedTask; }),
            new StoreMigration(2, 3, (_, _) => throw new InvalidOperationException("step 2 blew up")),
        ];
        var sut = NewSequence(migrations: migrations);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.MigrateAsync(CancellationToken.None));

        Assert.True(firstStepRan);
        Assert.Equal(1, ManifestCodec.Read(File.ReadAllText(Path.Combine(_dataDir, "manifest.json"))));
    }

    /// <summary>
    /// Beyond the six pinned behaviours — added with its own inventory line per the brief's rule
    /// ("any test beyond an inventory line gets a new inventory line appended in the same
    /// commit"). Exercises <see cref="StartupSequence.SweepRegistryAsync"/> itself: without this,
    /// the promote/demote wiring that <see cref="IStartupSequence.SweepRegistryAsync"/> exists to
    /// provide would ship with no test touching it at all.
    /// </summary>
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

        var swept = Assert.Single(store.Read().Tasks);
        Assert.Empty(swept.Tags.LooseTags);
        Assert.Equal(new TagValue("garage"), Assert.Single(swept.Tags.On(new DimensionId("loc"))));
    }
}
