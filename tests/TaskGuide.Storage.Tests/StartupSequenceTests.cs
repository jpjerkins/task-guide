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

    /// <summary>
    /// ADR-0009's phase rule, as an assertion: a conscious refusal is raised before the first
    /// write, so the data directory still holds exactly what it held when RunAsync was called.
    /// A whole listing rather than an absence check on named files — the point is to catch the
    /// next file some future lane adds on this path, which an absence check by definition cannot.
    /// </summary>
    private void AssertNothingWasWritten(params string[] expected) =>
        Assert.Equal(expected, Directory.GetFiles(_dataDir).Select(Path.GetFileName).Cast<string>().Order().ToArray());

    private StartupSequence NewSequence(
        DimensionRegistry? registry = null,
        IReadOnlyList<StoreMigration>? migrations = null,
        Func<string, CancellationToken, Task>? signal = null,
        JsonStore? store = null,
        IIdMinter? idMinter = null) =>
        new(
            store ?? new JsonStore(_dataDir),
            _dataDir,
            registry ?? SingleDimensionRegistry(),
            new SnapshotWriter(_dataDir),
            migrations ?? [],
            () => FixedNow,
            signal ?? ((_, _) => Task.CompletedTask),
            idMinter ?? new FakeIdMinter());

    /// <summary>Deterministic, collision-free within one test: a counter per id kind, not
    /// randomness — a seed test that asserted on a specific id would be pinning ULID randomness,
    /// which is not what any seed test here checks.</summary>
    private sealed class FakeIdMinter : IIdMinter
    {
        private int _dayTemplates;
        private int _patterns;

        public TaskId NextTaskId() => throw new NotSupportedException();
        public WindowId NextWindowId() => throw new NotSupportedException();
        public DayTemplateId NextDayTemplateId() => new($"dt_fake{++_dayTemplates:D20}");
        public PatternId NextPatternId() => new($"p_fake{++_patterns:D23}");
        public EventId NextEventId() => throw new NotSupportedException();
        public EventPrototypeId NextEventPrototypeId() => throw new NotSupportedException();
    }

    /// <summary>Writes `patterns.json` directly, bypassing <see cref="JsonStore.MutateAsync"/> —
    /// mirrors <see cref="SeedTasksFileRaw"/>: a <see cref="JsonStore"/> constructed over this
    /// starts with a non-empty <see cref="PatternBook"/> already on disk, as if from a prior
    /// startup, uncontaminated by the seed under test.</summary>
    private void SeedPatternsFileRaw(PatternBook book)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            PatternCodec.Write(writer, book);
        }

        File.WriteAllBytes(Path.Combine(_dataDir, "patterns.json"), buffer.ToArray());
    }

    private void SeedDayTemplatesFileRaw(IReadOnlyList<DayTemplate> templates)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            DayTemplateCodec.Write(writer, templates);
        }

        File.WriteAllBytes(Path.Combine(_dataDir, "day-templates.json"), buffer.ToArray());
    }

    /// <summary>Writes `tasks.json` directly, bypassing <see cref="JsonStore.MutateAsync"/> — so a
    /// <see cref="JsonStore"/> constructed over it starts with <see cref="IStore.LastWriteSucceeded"/>
    /// still <c>null</c>, letting a test observe whether a later call issues the store's first
    /// write, uncontaminated by the seed itself.</summary>
    private void SeedTasksFileRaw(IReadOnlyList<TaskItem> tasks)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            TaskCodec.Write(writer, tasks);
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
        AssertNothingWasWritten("manifest.json");
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
        AssertNothingWasWritten("manifest.json");
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

    // The migration-cycle case that used to live here is gone, not lost: per ADR-0009 a
    // non-monotonic step cannot be constructed at all, so this test can no longer build its own
    // fixture. It is now StoreMigrationTests, where a property of one step belongs — driving a
    // whole StartupSequence to prove it was always coverage at the wrong level.

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
        AssertNothingWasWritten("manifest.json");
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

    /// <summary>
    /// Task 8c, Part 1's regression net. <see cref="PatternBook.Active"/> calls
    /// <c>Patterns.Single(...)</c>, which throws on an empty list — a genuinely fresh `/data`,
    /// before this seed exists, crashes here on the first day-shape read. Deliberately does not
    /// assert anything about what the default Pattern's Day template contains (name, Windows) —
    /// only that <c>Active</c> does not throw and that every weekday's reference actually resolves
    /// to a Day template that exists — so this test still holds if the default Pattern's content
    /// changes later. The mutation that must kill it: make the seed a no-op.
    /// </summary>
    [Fact]
    public async Task An_empty_data_dir_starts_and_the_active_Pattern_resolves_without_throwing()
    {
        var sut = NewSequence();

        await sut.RunAsync(CancellationToken.None);

        // A fresh reload, not the original in-process store: the guarantee is that a *later*
        // startup against this now-non-empty `/data` also serves without throwing, which only a
        // reload proves — an in-memory view could look fine while the file on disk stayed empty.
        var view = new JsonStore(_dataDir).Read();
        var active = view.Patterns.Active; // throws here if the seed never landed

        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            var templateId = active[day];
            Assert.Contains(view.DayTemplates, t => t.Id == templateId);
        }
    }

    /// <summary>
    /// Task 8c, Part 1's actual seed content — a choice, not the guarantee (see the test above for
    /// that). "Vanilla"/"plain": no Availability Windows and no Event prototypes at all, since any
    /// authored Window or prototype would assert a specific schedule opinion a brand-new store has
    /// no basis for; the seven-days-of-one-template shape is the brief's own description of
    /// "nothing opinionated". The seed goes through <c>IStore.MutateAsync</c> (ruled — a direct
    /// file write would leave the already-loaded, empty <see cref="PatternBook"/> in memory,
    /// still crashing <c>.Active</c>), so a fresh reload is what proves it reached disk.
    /// </summary>
    [Fact]
    public async Task A_fresh_data_dir_seeds_one_vanilla_weekly_Pattern_of_a_single_plain_Day_template()
    {
        var sut = NewSequence();

        await sut.RunAsync(CancellationToken.None);

        var reloaded = new JsonStore(_dataDir).Read();
        var pattern = Assert.Single(reloaded.Patterns.Patterns);
        Assert.Equal(pattern.Id, reloaded.Patterns.ActivePatternId);
        Assert.Equal(7, pattern.Days.Count);
        Assert.Single(pattern.Days.Distinct()); // one plain template, referenced seven times

        var template = Assert.Single(reloaded.DayTemplates);
        Assert.Equal(pattern.Days[0], template.Id);
        Assert.Empty(template.Windows);
        Assert.Empty(template.EventPrototypes);
    }

    /// <summary>
    /// Ruled: an empty store has nothing for a snapshot to protect, matching the reasoning already
    /// accepted for 8b's fresh-store `manifest.json` bootstrap.
    /// </summary>
    [Fact]
    public async Task The_default_Pattern_seed_takes_no_snapshot()
    {
        var sut = NewSequence();

        await sut.RunAsync(CancellationToken.None);

        Assert.False(Directory.Exists(SnapshotsDir));
    }

    /// <summary>
    /// The golden store (and any store that already has a Pattern) must not be reseeded — a second
    /// Pattern materializing out of nowhere would itself be a correctness bug, independent of the
    /// golden-store fixture rule. Also pins that the seed issues no <c>MutateAsync</c> call at all
    /// when there is nothing to seed: <see cref="IStore.LastWriteSucceeded"/> stays <c>null</c>
    /// (mirrors <see cref="The_registry_sweep_makes_no_MutateAsync_call_when_nothing_moved"/>'s
    /// discipline — "no write attempted" is a different, stronger claim than "no visible change").
    /// </summary>
    [Fact]
    public async Task A_store_that_already_has_a_Pattern_is_never_reseeded()
    {
        var existingTemplate = new DayTemplate(new DayTemplateId("dt_existing00000000000001"), "Weekday", [], []);
        SeedDayTemplatesFileRaw([existingTemplate]);
        var existingPatternId = new PatternId("p_existing000000000000000001");
        var existingPattern = new Pattern(existingPatternId, "Only pattern", Enumerable.Repeat(existingTemplate.Id, 7).ToArray());
        SeedPatternsFileRaw(new PatternBook(existingPatternId, [existingPattern]));

        var store = new JsonStore(_dataDir);
        Assert.Null(store.LastWriteSucceeded);

        var sut = NewSequence(store: store);
        await sut.RunAsync(CancellationToken.None);

        Assert.Null(store.LastWriteSucceeded);

        var reloaded = new JsonStore(_dataDir).Read();
        var onlyPattern = Assert.Single(reloaded.Patterns.Patterns);
        Assert.Equal(existingPatternId, onlyPattern.Id);
        Assert.Equal(existingPatternId, reloaded.Patterns.ActivePatternId);
        Assert.Single(reloaded.DayTemplates);
    }
}
