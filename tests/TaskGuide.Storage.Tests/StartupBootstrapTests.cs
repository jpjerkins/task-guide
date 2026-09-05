using System.Text.Json;
using OneOf;
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
/// ADR-0009's phase split: <see cref="StartupPlanner"/> (plan, may refuse, cannot write),
/// <see cref="StartupWriter"/> (apply, cannot refuse), and <see cref="StartupBootstrap"/> (plan →
/// apply → open), superseding <c>StartupSequence</c>. Against `tests/TEST-INVENTORY.md`'s
/// "Sequential · TaskGuide.Storage.Tests" section.
/// </summary>
public sealed class StartupBootstrapTests : IDisposable
{
    private readonly string _dataDir = Directory.CreateTempSubdirectory("taskguide-startup-tests-").FullName;
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 28, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeProvider FixedClock = new FixedTimeProvider(FixedNow);

    public void Dispose() => Directory.Delete(_dataDir, recursive: true);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

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
    /// write, so the data directory still holds exactly what it held when the bootstrap was
    /// called. A whole listing rather than an absence check on named files — the point is to
    /// catch the next file some future lane adds on this path, which an absence check by
    /// definition cannot.
    /// </summary>
    private void AssertNothingWasWritten(params string[] expected) =>
        Assert.Equal(expected, Directory.GetFiles(_dataDir).Select(Path.GetFileName).Cast<string>().Order().ToArray());

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

    /// <summary>A Pattern referencing a Day template id that is never written to disk — legal,
    /// since neither <see cref="PatternBook"/> nor its codec validates that the reference
    /// resolves. Seeded purely so <see cref="StartupPlanner"/>'s "no Patterns" seed condition is
    /// already false, keeping the seed out of a test that is only about the sweep.</summary>
    private void SeedUnrelatedPatternFileRaw()
    {
        var pattern = new Pattern(new PatternId("p_unrelated00000000000000001"), "Placeholder", Enumerable.Repeat(new DayTemplateId("dt_unrelated0000000000000001"), 7).ToArray());
        SeedPatternsFileRaw(new PatternBook(pattern.Id, [pattern]));
    }

    /// <summary>
    /// Mirrors <see cref="StartupBootstrap.BootstrapAndOpenStoreAsync"/>'s plan-then-apply
    /// orchestration, but against a caller-supplied <see cref="JsonStore"/> instance rather than
    /// opening a fresh one afterward — for tests that need to inspect that exact instance's
    /// <see cref="IStore.LastWriteSucceeded"/> or in-memory view after the run, which
    /// <see cref="StartupBootstrap"/>'s public contract (a brand-new store) cannot expose.
    /// </summary>
    private async Task RunAsync(
        JsonStore store,
        DimensionRegistry? registry = null,
        IReadOnlyList<StoreMigration>? migrations = null,
        Func<string, CancellationToken, Task>? signal = null,
        IIdMinter? idMinter = null)
    {
        var planner = new StartupPlanner(_dataDir, registry ?? SingleDimensionRegistry(), migrations ?? [], idMinter ?? new FakeIdMinter());
        var result = planner.Plan(store);

        if (result.IsT1)
        {
            var refusal = result.AsT1;
            if (refusal.IsT0)
            {
                var collision = refusal.AsT0;
                if (signal is not null) await signal(collision.Message, CancellationToken.None);
                throw new DuplicateDimensionValueException(collision.Message, []);
            }

            var versionAhead = refusal.AsT1;
            throw new StoreVersionAheadException(versionAhead.StoredVersion, versionAhead.CurrentVersion);
        }

        var writer = new StartupWriter(store, _dataDir, new SnapshotWriter(_dataDir), FixedClock);
        await writer.ApplyAsync(result.AsT0, CancellationToken.None);
    }

    private Task<IStore> BootstrapAsync(
        DimensionRegistry? registry = null,
        IReadOnlyList<StoreMigration>? migrations = null,
        Func<string, CancellationToken, Task>? signal = null,
        IIdMinter? idMinter = null) =>
        StartupBootstrap.BootstrapAndOpenStoreAsync(
            _dataDir,
            registry ?? SingleDimensionRegistry(),
            migrations ?? [],
            idMinter ?? new FakeIdMinter(),
            FixedClock,
            signal ?? ((_, _) => Task.CompletedTask),
            CancellationToken.None);

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

        await RunAsync(new JsonStore(_dataDir), migrations: migrations);

        Assert.Equal([-1, 0], order);
        Assert.Equal(1, ManifestCodec.Read(File.ReadAllText(ManifestPath)));
    }

    [Fact]
    public async Task A_snapshot_is_written_once_per_startup_and_only_when_that_startup_will_write()
    {
        // Nothing pending: no manifest mismatch, no fake migrations, registry has nothing to sweep.
        WriteManifest(1);
        await BootstrapAsync();
        Assert.False(Directory.Exists(SnapshotsDir));

        // A pending migration exists (0 → 1, landing exactly on CurrentVersion): the startup will
        // write, so exactly one snapshot is taken.
        Directory.Delete(_dataDir, recursive: true);
        Directory.CreateDirectory(_dataDir);
        WriteManifest(0);
        IReadOnlyList<StoreMigration> migrations = [new StoreMigration(0, 1, (_, _) => Task.CompletedTask)];

        await BootstrapAsync(migrations: migrations);

        Assert.True(Directory.Exists(SnapshotsDir));
        Assert.Single(Directory.GetDirectories(SnapshotsDir));
    }

    [Fact]
    public async Task A_registry_collision_signals_outbound_before_exiting()
    {
        WriteManifest(1);
        var signalled = new List<string>();

        await Assert.ThrowsAsync<DuplicateDimensionValueException>(() => BootstrapAsync(
            registry: CollidingRegistry(),
            signal: (message, _) => { signalled.Add(message); return Task.CompletedTask; }));

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

        await BootstrapAsync(migrations: migrations);

        Assert.False(ran);
        Assert.False(Directory.Exists(SnapshotsDir));
        Assert.Equal(ManifestCodec.CurrentVersion, ManifestCodec.Read(File.ReadAllText(ManifestPath)));
    }

    [Fact]
    public async Task A_version_ahead_of_this_binary_refuses_to_start_named()
    {
        WriteManifest(ManifestCodec.CurrentVersion + 1);

        var ex = await Assert.ThrowsAsync<StoreVersionAheadException>(() => BootstrapAsync());

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

        await Assert.ThrowsAsync<InvalidOperationException>(() => BootstrapAsync(migrations: migrations));

        Assert.True(firstStepRan);
        Assert.Equal(-1, ManifestCodec.Read(File.ReadAllText(ManifestPath)));
    }

    /// <summary>
    /// I2 (review, carried over): a walk that would land past `CurrentVersion` must refuse rather
    /// than write a `manifest.json` this very binary would then refuse to start on its next boot.
    /// </summary>
    [Fact]
    public async Task A_migration_walk_that_would_overshoot_CurrentVersion_refuses_to_start()
    {
        WriteManifest(ManifestCodec.CurrentVersion);
        IReadOnlyList<StoreMigration> migrations =
        [
            new StoreMigration(ManifestCodec.CurrentVersion, ManifestCodec.CurrentVersion + 1, (_, _) => Task.CompletedTask),
        ];

        var ex = await Assert.ThrowsAsync<StoreVersionAheadException>(() => BootstrapAsync(migrations: migrations));

        Assert.Equal(ManifestCodec.CurrentVersion + 1, ex.StoredVersion);
        Assert.Equal(ManifestCodec.CurrentVersion, ManifestCodec.Read(File.ReadAllText(ManifestPath)));
        AssertNothingWasWritten("manifest.json");
    }

    /// <summary>
    /// I4 (review, carried over) + M-c: a fresh `/data` — no `manifest.json` at all — establishes
    /// one at `CurrentVersion` without a snapshot: nothing pending, nothing swept, and the seed
    /// alone never triggers a snapshot (an empty store has nothing for a snapshot to protect).
    /// </summary>
    [Fact]
    public async Task Startup_against_a_fresh_data_dir_creates_manifest_json_without_snapshotting()
    {
        await BootstrapAsync();

        Assert.True(File.Exists(ManifestPath));
        Assert.Equal(ManifestCodec.CurrentVersion, ManifestCodec.Read(File.ReadAllText(ManifestPath)));
        Assert.False(Directory.Exists(SnapshotsDir));
    }

    /// <summary>M-a (carried over): the "writes nothing when nothing moved" guarantee, observed at
    /// the <see cref="IStore"/> level rather than by inspecting <see cref="StartupPlanner"/>'s
    /// internals — <see cref="IStore.LastWriteSucceeded"/> stays <c>null</c> (no write ever
    /// attempted) rather than merely "no visible change". A Pattern is seeded up front so the
    /// default-Pattern seed itself does not also want to write here — this test is about the
    /// sweep's half of the "nothing moved" guarantee.</summary>
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
        SeedUnrelatedPatternFileRaw();

        var store = new JsonStore(_dataDir);
        Assert.Null(store.LastWriteSucceeded);

        await RunAsync(store);

        Assert.Null(store.LastWriteSucceeded);
    }

    /// <summary>
    /// Beyond the six pinned behaviours — added with its own inventory line per the brief's rule
    /// ("any test beyond an inventory line gets a new inventory line appended in the same
    /// commit"). Exercises the sweep half of <see cref="StartupPlanner.Plan"/>: without this, the
    /// promote/demote wiring it exists to provide would ship with no test touching it at all.
    /// </summary>
    /// <remarks>
    /// Asserts against a freshly-constructed <see cref="JsonStore"/> reloaded from `tasks.json` —
    /// not the instance the sweep ran against — which is what actually proves the change reached
    /// disk rather than only the in-process view. Also asserts `day-templates.json` was never
    /// created: an unrelated Pattern is seeded up front (see <see cref="SeedUnrelatedPatternFileRaw"/>)
    /// so the default-Pattern seed does not fire and add one of its own, which would make this
    /// assertion meaningless.
    /// </remarks>
    [Fact]
    public async Task The_registry_sweep_promotes_a_loose_Tag_the_registry_now_claims_and_writes_the_change()
    {
        SeedUnrelatedPatternFileRaw();
        var store = new JsonStore(_dataDir);
        var looseTagged = new TaskItem(
            new TaskId("t_test0000000000000000001"),
            "Sweep me",
            null,
            new TagSet(new Dictionary<DimensionId, IReadOnlyList<TagValue>>(), [new LooseTag("garage")]),
            null, null, null, null,
            DateTimeOffset.UtcNow);
        await store.MutateAsync<Never>(_ => new StoreMutation([new TasksWrite([looseTagged])]), CancellationToken.None);

        await RunAsync(store);

        var reloaded = new JsonStore(_dataDir);
        var swept = Assert.Single(reloaded.Read().Tasks);
        Assert.Empty(swept.Tags.LooseTags);
        Assert.Equal(new TagValue("garage"), Assert.Single(swept.Tags.On(new DimensionId("loc"))));
        Assert.False(File.Exists(Path.Combine(_dataDir, "day-templates.json")));
    }

    /// <summary>
    /// I3 (review, carried over): three of the four swept collections (Day templates, Overrides,
    /// Events) shipped with zero coverage. This pins Day template Window Tags specifically —
    /// Overrides and Events share the same <see cref="RegistrySweep.Sweep"/> call and the same
    /// change-detection, so one non-Task collection closes the coverage gap. An unrelated Pattern
    /// is seeded so the default-Pattern seed does not also add a second Day template here.
    /// </summary>
    [Fact]
    public async Task The_registry_sweep_promotes_a_loose_Tag_on_a_Day_template_Window()
    {
        SeedUnrelatedPatternFileRaw();
        var store = new JsonStore(_dataDir);
        var looseTaggedWindow = new AvailabilityWindow(
            new WindowId("w_test000000000000000001"),
            "Evening",
            new TimeOnly(18, 0),
            new TimeOnly(21, 0),
            new TagSet(new Dictionary<DimensionId, IReadOnlyList<TagValue>>(), [new LooseTag("garage")]));
        var template = new DayTemplate(new DayTemplateId("dt_test00000000000000001"), "Weekday", [looseTaggedWindow], []);
        await store.MutateAsync<Never>(_ => new StoreMutation([new DayTemplatesWrite([template])]), CancellationToken.None);

        await RunAsync(store);

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
        var opened = await BootstrapAsync();

        var view = opened.Read();
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
    /// "nothing opinionated".
    /// </summary>
    [Fact]
    public async Task A_fresh_data_dir_seeds_one_vanilla_weekly_Pattern_of_a_single_plain_Day_template()
    {
        var opened = await BootstrapAsync();

        var view = opened.Read();
        var pattern = Assert.Single(view.Patterns.Patterns);
        Assert.Equal(pattern.Id, view.Patterns.ActivePatternId);
        Assert.Equal(7, pattern.Days.Count);
        Assert.Single(pattern.Days.Distinct()); // one plain template, referenced seven times

        var template = Assert.Single(view.DayTemplates);
        Assert.Equal(pattern.Days[0], template.Id);
        Assert.Empty(template.Windows);
        Assert.Empty(template.EventPrototypes);
    }

    /// <summary>
    /// Ruled: an empty store has nothing for a snapshot to protect, matching the reasoning already
    /// accepted for the fresh-store `manifest.json` bootstrap.
    /// </summary>
    [Fact]
    public async Task The_default_Pattern_seed_takes_no_snapshot()
    {
        await BootstrapAsync();

        Assert.False(Directory.Exists(SnapshotsDir));
    }

    /// <summary>
    /// The golden store (and any store that already has a Pattern) must not be reseeded — a second
    /// Pattern materializing out of nowhere would itself be a correctness bug, independent of the
    /// golden-store fixture rule. Also pins that the seed issues no <c>MutateAsync</c> call at all
    /// when there is nothing to seed: <see cref="IStore.LastWriteSucceeded"/> stays <c>null</c>.
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

        await RunAsync(store);

        Assert.Null(store.LastWriteSucceeded);

        var reloaded = new JsonStore(_dataDir).Read();
        var onlyPattern = Assert.Single(reloaded.Patterns.Patterns);
        Assert.Equal(existingPatternId, onlyPattern.Id);
        Assert.Equal(existingPatternId, reloaded.Patterns.ActivePatternId);
        Assert.Single(reloaded.DayTemplates);
    }

    /// <summary>
    /// Added for #78: the plan phase's own contract, independent of the exception translation
    /// <see cref="StartupBootstrap"/> layers on top — <see cref="StartupPlanner.Plan"/> returns its
    /// refusal as a value, it does not throw, and nothing has been written by the time it returns,
    /// for all three refusal paths a version-ahead check can take.
    /// </summary>
    [Fact]
    public void The_plan_phase_returns_its_refusal_rather_than_throwing_and_writes_nothing()
    {
        // 1. Registry collision.
        WriteManifest(1);
        var collisionPlanner = new StartupPlanner(_dataDir, CollidingRegistry(), [], new FakeIdMinter());
        var collisionResult = collisionPlanner.Plan(new JsonStore(_dataDir));
        Assert.True(collisionResult.IsT1);
        Assert.True(collisionResult.AsT1.IsT0);
        AssertNothingWasWritten("manifest.json");

        // 2. Version already on disk is ahead of this binary.
        Directory.Delete(_dataDir, recursive: true);
        Directory.CreateDirectory(_dataDir);
        WriteManifest(ManifestCodec.CurrentVersion + 1);
        var aheadPlanner = new StartupPlanner(_dataDir, SingleDimensionRegistry(), [], new FakeIdMinter());
        var aheadResult = aheadPlanner.Plan(new JsonStore(_dataDir));
        Assert.True(aheadResult.IsT1);
        Assert.True(aheadResult.AsT1.IsT1);
        AssertNothingWasWritten("manifest.json");

        // 3. A migration walk that would overshoot CurrentVersion.
        Directory.Delete(_dataDir, recursive: true);
        Directory.CreateDirectory(_dataDir);
        WriteManifest(ManifestCodec.CurrentVersion);
        IReadOnlyList<StoreMigration> overshoot = [new StoreMigration(ManifestCodec.CurrentVersion, ManifestCodec.CurrentVersion + 1, (_, _) => Task.CompletedTask)];
        var overshootPlanner = new StartupPlanner(_dataDir, SingleDimensionRegistry(), overshoot, new FakeIdMinter());
        var overshootResult = overshootPlanner.Plan(new JsonStore(_dataDir));
        Assert.True(overshootResult.IsT1);
        Assert.True(overshootResult.AsT1.IsT1);
        AssertNothingWasWritten("manifest.json");
    }

    /// <summary>
    /// Added for #78: pins the apply phase's own ordering — snapshot, then migrate, then stamp the
    /// manifest, then write — using an instrumented migration step that records what it observes
    /// mid-flight, plus the final state.
    /// </summary>
    [Fact]
    public async Task A_valid_plan_snapshots_migrates_stamps_the_manifest_then_writes_in_that_order()
    {
        WriteManifest(0);
        File.WriteAllText(Path.Combine(_dataDir, "tasks.json"), "[]"); // something for the snapshot to copy

        var observedManifestVersion = -1;
        var snapshotDirExistedDuringStep = false;
        IReadOnlyList<StoreMigration> migrations =
        [
            new StoreMigration(0, 1, (dir, _) =>
            {
                snapshotDirExistedDuringStep = Directory.Exists(SnapshotsDir);
                observedManifestVersion = ManifestCodec.Read(File.ReadAllText(Path.Combine(dir, "manifest.json")));
                return Task.CompletedTask;
            }),
        ];
        SeedUnrelatedPatternFileRaw(); // keep the seed out of this test's write

        var store = new JsonStore(_dataDir);
        await RunAsync(store, migrations: migrations);

        Assert.True(snapshotDirExistedDuringStep);
        Assert.Equal(0, observedManifestVersion); // still pre-migration when the step ran
        Assert.Equal(1, ManifestCodec.Read(File.ReadAllText(ManifestPath)));
    }

    /// <summary>
    /// Added for #78: the reload guarantee <see cref="StartupBootstrap"/> exists to provide. A
    /// migration step here writes `tasks.json` directly to disk, bypassing <see cref="IStore.MutateAsync{T}"/>
    /// entirely — the bootstrap instance's own in-memory view, loaded before this step ran, cannot
    /// see it. Only the runtime store <see cref="StartupBootstrap.BootstrapAndOpenStoreAsync"/>
    /// opens afterward, freshly, does.
    /// </summary>
    [Fact]
    public async Task The_runtime_store_opened_after_the_write_phase_reads_what_the_write_phase_landed()
    {
        WriteManifest(0);
        var migratedTask = new TaskItem(
            new TaskId("t_migrated000000000000001"),
            "Landed by a migration step",
            null,
            TagSet.Empty,
            null, null, null, null,
            DateTimeOffset.UtcNow);
        IReadOnlyList<StoreMigration> migrations =
        [
            new StoreMigration(0, 1, (dir, _) =>
            {
                using var buffer = new MemoryStream();
                using (var writer = new Utf8JsonWriter(buffer))
                {
                    TaskCodec.Write(writer, [migratedTask]);
                }

                File.WriteAllBytes(Path.Combine(dir, "tasks.json"), buffer.ToArray());
                return Task.CompletedTask;
            }),
        ];

        var opened = await BootstrapAsync(migrations: migrations);

        var landed = Assert.Single(opened.Read().Tasks);
        Assert.Equal(migratedTask.Id, landed.Id);
    }

    /// <summary>Added for #78: ADR-0008's floor — an empty store must start, and its day shape
    /// must be readable, driven against the actual store <see cref="StartupBootstrap"/> opens.</summary>
    [Fact]
    public async Task An_empty_data_dir_bootstraps_and_IDayShapeReader_returns_a_usable_DayShape()
    {
        var opened = await BootstrapAsync();
        var reader = new DayShapeReader(opened);

        var shape = reader.For(new DateOnly(2026, 8, 28));

        Assert.Empty(shape.Windows);
        Assert.Empty(shape.Events);
        Assert.False(shape.IsOverridden);
    }
}
