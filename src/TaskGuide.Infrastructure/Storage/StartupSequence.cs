using System.Text.Json;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;

namespace TaskGuide.Infrastructure.Storage;

/// <summary>
/// <b>assert → seed → snapshot → migrate → sweep.</b> Composes 8a's <see cref="ManifestCodec"/> and
/// <see cref="SnapshotWriter"/> with the Dimension registry (#21), the default-Pattern seed (#52),
/// and <see cref="RegistrySweep"/> into the one orchestrated startup phase ADR-0001 says
/// memory-authoritative depends on.
/// </summary>
/// <remarks>
/// The four <see cref="IStartupSequence"/> members are independently callable and each does
/// exactly its own job; <see cref="RunAsync"/> is the only place that decides <em>whether</em> a
/// step needs to run at all, because that decision needs to see across steps — "only when that
/// startup will write" cannot be answered by <see cref="SnapshotAsync"/> in isolation.
/// </remarks>
public sealed class StartupSequence(
    IStore store,
    string dataDir,
    DimensionRegistry registry,
    SnapshotWriter snapshotWriter,
    IReadOnlyList<StoreMigration> migrations,
    Func<DateTimeOffset> now,
    Func<string, CancellationToken, Task> signalRegistryCollision,
    IIdMinter idMinter)
    : IStartupSequence
{
    /// <summary>
    /// The top-level collection files a migration or a registry sweep could touch. `manifest.json`
    /// travels with them, always — per the Backup entry, a set restored without it hands
    /// already-migrated data to a binary that migrates it again. `completions/*` and `fires/*` are
    /// excluded: neither carries a Dimension value, and no migration step exists yet that would
    /// touch either.
    /// </summary>
    private static readonly string[] CollectionFileNames =
    [
        "manifest.json",
        "tasks.json",
        "day-templates.json",
        "patterns.json",
        "overrides.json",
        "events.json",
        "event-exceptions.json",
    ];

    /// <summary>
    /// The vanilla default weekly Pattern: seven days of one plain Day template, carrying no
    /// Availability Windows and no Event prototypes — any authored Window or prototype would
    /// assert a schedule opinion a brand-new store has no basis for. Runs only when the loaded
    /// <see cref="PatternBook"/> has <em>no Patterns</em>, not merely when `patterns.json` is
    /// absent: a present-but-empty `patterns.json` crashes <see cref="PatternBook.Active"/> exactly
    /// the same way an absent file does, and this is the condition that actually distinguishes
    /// "nothing to make <c>.Active</c> safe" from "already seeded". The golden store and any store
    /// that already has a Pattern is untouched by this check.
    /// </summary>
    /// <remarks>
    /// Goes through <see cref="IStore.MutateAsync"/>, not a direct file write (ruled): that is what
    /// swaps the already-loaded in-memory view in the same act as the disk write — a raw file write
    /// would leave <see cref="_current"/>'s already-loaded, empty <see cref="PatternBook"/> in
    /// memory, still crashing <c>.Active</c> on the very next read. Builds the Day templates write
    /// from the freshly-read view rather than an empty list: a store whose Pattern collection is
    /// empty but whose Day template collection already has entries (a hand-edited or partially
    /// corrupted store) must not have those entries silently erased by this seed.
    /// <para>
    /// <b>Caveat, unreachable today (fix round 1, review's Minor finding):</b> this write runs in
    /// <see cref="RunAsync"/> <em>before</em> the snapshot decision, and is never folded into that
    /// decision's `willWrite` check — by design, since the seed itself takes no snapshot. But if a
    /// store is <em>not</em> fresh (`manifest.json` present, migrations pending) and <em>also</em>
    /// has an empty Pattern collection, this write still lands first, so a snapshot taken
    /// afterward for the migration would protect a `patterns.json`/`day-templates.json` this seed
    /// already rewrote, not what was on disk at boot. Not a live bug: <see cref="StoreMigrations.Ordered"/>
    /// is empty today, so "fresh Patterns + pending migration" cannot co-occur, and this seed only
    /// ever adds records (see above) — the pre-seed `patterns.json` in that scenario is the broken,
    /// crashing state this seed exists to replace, so there is no recoverable prior state a snapshot
    /// one step earlier would have protected that this one doesn't. Worth revisiting before a real
    /// migration step touching `patterns.json` ships.
    /// </para>
    /// </remarks>
    private async Task SeedDefaultPatternAsync(CancellationToken cancellationToken)
    {
        var view = store.Read();
        if (view.Patterns.Patterns.Count > 0) return;

        var template = new DayTemplate(idMinter.NextDayTemplateId(), "Ordinary day", [], []);
        var days = Enumerable.Repeat(template.Id, 7).ToArray();
        var pattern = new Pattern(idMinter.NextPatternId(), "Default", days);
        var book = new PatternBook(pattern.Id, [pattern]);

        await store.MutateAsync(_ => new StoreMutation(
        [
            new DayTemplatesWrite([.. view.DayTemplates, template]),
            new PatternsWrite(book),
        ]), cancellationToken);
    }

    public void AssertRegistry() => registry.AssertNoDuplicateValues();

    /// <summary>
    /// Copies every collection file that currently exists into a new `snapshots/&lt;utc&gt;/`
    /// directory. A file named in <see cref="CollectionFileNames"/> but absent (a fresh `/data`,
    /// which starts with almost none of them) is skipped rather than handed to
    /// <see cref="SnapshotWriter.TakeAsync"/>, which throws <see cref="FileNotFoundException"/> on
    /// a missing source — there is nothing on disk yet for a missing file to protect.
    /// </summary>
    public async Task SnapshotAsync(CancellationToken cancellationToken)
    {
        var existing = CollectionFileNames.Where(name => File.Exists(Path.Combine(dataDir, name))).ToArray();
        if (existing.Length == 0) return;

        await snapshotWriter.TakeAsync(existing, now(), cancellationToken);
    }

    /// <summary>
    /// Reads `manifest.json`, refuses immediately (no step attempted) if its version is ahead of
    /// this binary's <see cref="ManifestCodec.CurrentVersion"/>, then walks the ordered N→N+1
    /// steps supplied at construction. `manifest.json` is written exactly once, after every step
    /// has succeeded — a step that throws leaves the file at its pre-migration version, so the
    /// whole walk (not a partial one) is what a retried startup attempts again.
    /// </summary>
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        var (storedVersion, pending) = PlanMigration();

        if (pending.Count == 0)
        {
            // A missing manifest.json is a fresh `/data`, never migrated by any binary — there is
            // nothing to walk, but the file itself still needs to exist so a later startup has a
            // version to read. Establishing it here is not a migration touching existing data
            // (there is none yet), which is why RunAsync does not snapshot for this case.
            if (storedVersion is null)
            {
                await WriteManifestAsync(ManifestCodec.CurrentVersion, cancellationToken);
            }

            return;
        }

        foreach (var step in pending)
        {
            await step.Apply(dataDir, cancellationToken);
        }

        // The walk's own endpoint, not the constant CurrentVersion: `migrations` is supplied at
        // construction (StoreMigrations.Ordered in production, empty today) and the manifest
        // reflects wherever the ordered steps actually landed.
        await WriteManifestAsync(pending[^1].To, cancellationToken);
    }

    /// <summary>
    /// Applies <see cref="RegistrySweep.Sweep"/> to every Tag-bearing collection — Tasks, Day
    /// template Windows and Event prototypes, Override Windows, and Events — and writes back only
    /// the collections that actually changed. A store where nothing promoted or demoted writes
    /// nothing at all: no <see cref="IStore.MutateAsync"/> call is made, not merely an empty one —
    /// <see cref="IStore.LastWriteSucceeded"/> documents the outcome of an actual disk write, and
    /// an empty <see cref="StoreMutation"/> would still (wrongly) report one.
    /// </summary>
    /// <remarks>
    /// The pre-check below is only a gate against calling <see cref="IStore.MutateAsync"/> for
    /// nothing. What actually gets written is computed <em>again</em>, inside the callback, from
    /// the view the callback itself supplies rather than reused from the pre-check — that is the
    /// atomic read-modify-write <see cref="JsonStore.MutateAsync"/>'s write lock exists to
    /// provide (see its remarks), and reusing a plan computed outside the lock would be the
    /// lost-update shape even though nothing races at today's single-threaded startup. It is also
    /// what makes this correct the day a migration step goes through the store instead of writing
    /// <c>dataDir</c> raw — see <see cref="RunAsync"/>'s remarks on the residual gap that is not
    /// fixed here.
    /// </remarks>
    public async Task SweepRegistryAsync(CancellationToken cancellationToken)
    {
        if (!ComputeSweepPlan(store.Read()).HasChanges) return;

        await store.MutateAsync(view =>
        {
            var plan = ComputeSweepPlan(view);

            var writes = new List<object>();
            if (plan.TasksChanged) writes.Add(new TasksWrite(plan.Tasks));
            if (plan.DayTemplatesChanged) writes.Add(new DayTemplatesWrite(plan.DayTemplates));
            if (plan.OverridesChanged) writes.Add(new OverridesWrite(plan.Overrides));
            if (plan.EventsChanged) writes.Add(new EventsWrite(plan.Events));

            return new StoreMutation(writes);
        }, cancellationToken);
    }

    /// <summary>
    /// The composition root's entry point: assert → snapshot → migrate → sweep. A registry
    /// collision signals outbound (awaited) before the exception propagates and nothing else
    /// runs. Otherwise the snapshot runs at most once, and only when a migration or a registry
    /// sweep is actually about to write.
    /// </summary>
    /// <remarks>
    /// <b>Known residual, not fixed here (I1 in the review):</b> <see cref="MigrateAsync"/>'s
    /// steps write raw files under <paramref name="dataDir"/>; <see cref="JsonStore"/>'s
    /// in-memory view is loaded once at construction (Task 7 is frozen) and cannot observe those
    /// writes. So the <c>store.Read()</c> calls below — the pre-check that decides whether to
    /// snapshot, and <see cref="SweepRegistryAsync"/>'s own re-read after migrating — both still
    /// see pre-migration data even though they run after <see cref="MigrateAsync"/> textually.
    /// The fix that <em>is</em> mine is sequencing and atomicity: the sweep's actual write-plan is
    /// computed strictly after the migration call, and strictly inside <see
    /// cref="IStore.MutateAsync"/>'s callback rather than from a separate, non-atomic
    /// <c>store.Read()</c> — see <see cref="SweepRegistryAsync"/>'s remarks. Closing the residual
    /// needs either a store-reload API or migrations that go through the store, both out of this
    /// unit's lane; recorded against the branch's final triage.
    /// </remarks>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            AssertRegistry();
        }
        catch (DuplicateDimensionValueException ex)
        {
            await signalRegistryCollision(ex.Message, cancellationToken);
            throw;
        }

        // Runs before the snapshot decision and is never folded into `willWrite` below: the
        // default-Pattern seed takes no snapshot (ruled — an empty store has nothing for a
        // snapshot to protect, same reasoning as the fresh-manifest bootstrap in MigrateAsync).
        await SeedDefaultPatternAsync(cancellationToken);

        // Reading version-ahead here, before any snapshot decision, is what makes that refusal
        // write nothing: PlanMigration throws before SnapshotAsync is ever considered.
        var (storedVersion, pendingMigrations) = PlanMigration();

        // Only a gate for "should this startup snapshot at all" — it has to run before anything
        // else does, so it can only see what is on disk right now. It is deliberately not reused
        // for the sweep's actual write below (see this method's remarks and
        // SweepRegistryAsync's).
        var willWrite = pendingMigrations.Count > 0 || ComputeSweepPlan(store.Read()).HasChanges;
        if (willWrite)
        {
            await SnapshotAsync(cancellationToken);
        }

        if (pendingMigrations.Count > 0 || storedVersion is null)
        {
            await MigrateAsync(cancellationToken);
        }

        // Unconditional: SweepRegistryAsync owns its own "nothing changed, nothing written"
        // guard. A second guard here would only be pinned in conjunction with that one (the
        // review's M-a/M19 finding), never individually — so there is exactly one guard, in
        // exactly one place, and it is the one the write actually goes through.
        await SweepRegistryAsync(cancellationToken);
    }

    /// <summary>
    /// Reads `manifest.json` (absent reads as "fresh, nothing to migrate") and walks
    /// <paramref name="migrations"/> forward from its version, one N→N+1 step at a time, for as
    /// long as a step starting at the current cursor exists. Throws
    /// <see cref="StoreVersionAheadException"/> immediately — no steps attempted — when the
    /// stored version already on disk is ahead of this binary's
    /// <see cref="ManifestCodec.CurrentVersion"/>: a rollback must not silently down-migrate.
    /// </summary>
    /// <remarks>
    /// The walk's own endpoint is not bounded by <see cref="ManifestCodec.CurrentVersion"/> while
    /// it runs — it stops when <paramref name="migrations"/> has nothing left to offer for the
    /// current cursor, which is what lets a test supply its own short list and land wherever that
    /// list says, without needing to fake the constant (see <see cref="StoreMigrations.Ordered"/>,
    /// empty today, for why production never notices). Two guardrails still apply to whatever the
    /// walk finds, both cheap because a well-formed N→N+1 list already satisfies them and neither
    /// reintroduces the "can't fake the constant" problem:
    /// <list type="bullet">
    /// <item>Each step must move the cursor <em>strictly forward</em> (<c>step.To &gt; cursor</c>).
    /// Without this, a cycle in <paramref name="migrations"/> (or the constructor's fake list)
    /// spins this loop forever — an infinite hang at startup, not an exception.</item>
    /// <item>The walk's landing version must not exceed <see cref="ManifestCodec.CurrentVersion"/>.
    /// Without this, a walk that outruns what this binary can run writes a `manifest.json` this
    /// very binary would refuse to start on next boot with <see cref="StoreVersionAheadException"/>
    /// — the overshoot is silent today because nothing checks the walk's own endpoint, only the
    /// version already on disk at the top of this method.</item>
    /// </list>
    /// A gap the walk cannot cross (no step registered for some cursor it reaches, short of
    /// <see cref="ManifestCodec.CurrentVersion"/>) is not treated as an error — the walk simply
    /// stops there, silently, which only matters once a real gap can exist; today's empty
    /// <see cref="StoreMigrations.Ordered"/> never produces one.
    /// </remarks>
    private (int? StoredVersion, IReadOnlyList<StoreMigration> Pending) PlanMigration()
    {
        var manifestPath = Path.Combine(dataDir, "manifest.json");
        if (!File.Exists(manifestPath)) return (null, []);

        var version = ManifestCodec.Read(File.ReadAllText(manifestPath));

        if (version > ManifestCodec.CurrentVersion)
        {
            throw new StoreVersionAheadException(version, ManifestCodec.CurrentVersion);
        }

        var pending = new List<StoreMigration>();
        var cursor = version;
        while (migrations.FirstOrDefault(m => m.From == cursor) is { } step)
        {
            if (step.To <= cursor)
            {
                throw new InvalidOperationException(
                    $"Migration step {step.From}→{step.To} does not move the version strictly forward; " +
                    "a non-monotonic step would let the walk cycle and hang startup.");
            }

            pending.Add(step);
            cursor = step.To;
        }

        if (pending.Count > 0 && cursor > ManifestCodec.CurrentVersion)
        {
            throw new StoreVersionAheadException(cursor, ManifestCodec.CurrentVersion);
        }

        return (version, pending);
    }

    private async Task WriteManifestAsync(int version, CancellationToken cancellationToken)
    {
        var path = Path.Combine(dataDir, "manifest.json");
        var tempPath = Path.Combine(dataDir, $".manifest.json.tmp-{Guid.NewGuid():N}");

        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    ManifestCodec.Write(writer, version);
                    await writer.FlushAsync(cancellationToken);
                }

                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private RegistrySweepPlan ComputeSweepPlan(IStoreView view)
    {
        var (tasks, tasksChanged) = SweepTasks(view.Tasks);
        var (templates, templatesChanged) = SweepDayTemplates(view.DayTemplates);
        var (overrides, overridesChanged) = SweepOverrides(view.Overrides);
        var (events, eventsChanged) = SweepEvents(view.Events);

        return new RegistrySweepPlan(
            tasks, tasksChanged,
            templates, templatesChanged,
            overrides, overridesChanged,
            events, eventsChanged);

        (IReadOnlyList<TaskItem>, bool) SweepTasks(IReadOnlyList<TaskItem> tasks)
        {
            var changed = false;
            var swept = tasks.Select(t =>
            {
                var newTags = RegistrySweep.Sweep(t.Tags, registry);
                if (!TagSetsEqual(t.Tags, newTags)) changed = true;
                return t with { Tags = newTags };
            }).ToArray();

            return (swept, changed);
        }

        (IReadOnlyList<DayTemplate>, bool) SweepDayTemplates(IReadOnlyList<DayTemplate> templates)
        {
            var changed = false;
            var swept = templates.Select(t =>
            {
                var windows = t.Windows.Select(w =>
                {
                    var newTags = RegistrySweep.Sweep(w.Tags, registry);
                    if (!TagSetsEqual(w.Tags, newTags)) changed = true;
                    return w with { Tags = newTags };
                }).ToArray();

                var prototypes = t.EventPrototypes.Select(p =>
                {
                    var newTags = RegistrySweep.Sweep(p.Tags, registry);
                    if (!TagSetsEqual(p.Tags, newTags)) changed = true;
                    return p with { Tags = newTags };
                }).ToArray();

                return t with { Windows = windows, EventPrototypes = prototypes };
            }).ToArray();

            return (swept, changed);
        }

        (IReadOnlyList<DateOverride>, bool) SweepOverrides(IReadOnlyList<DateOverride> overrides)
        {
            var changed = false;
            var swept = overrides.Select(o =>
            {
                var windows = o.Windows.Select(w =>
                {
                    var newTags = RegistrySweep.Sweep(w.Tags, registry);
                    if (!TagSetsEqual(w.Tags, newTags)) changed = true;
                    return w with { Tags = newTags };
                }).ToArray();

                return o with { Windows = windows };
            }).ToArray();

            return (swept, changed);
        }

        (IReadOnlyList<Event>, bool) SweepEvents(IReadOnlyList<Event> events)
        {
            var changed = false;
            var swept = events.Select(e =>
            {
                var newTags = RegistrySweep.Sweep(e.Tags, registry);
                if (!TagSetsEqual(e.Tags, newTags)) changed = true;
                return e with { Tags = newTags };
            }).ToArray();

            return (swept, changed);
        }
    }

    /// <summary>
    /// Content equality for a <see cref="TagSet"/>. Not record equality: <see cref="RegistrySweep.Sweep"/>
    /// always builds a fresh dictionary and list, so a reference/default record comparison would
    /// report "changed" on every call whether or not anything actually moved.
    /// </summary>
    private static bool TagSetsEqual(TagSet a, TagSet b)
    {
        if (!a.LooseTags.Select(t => t.Value).SequenceEqual(b.LooseTags.Select(t => t.Value))) return false;
        if (a.Dimensions.Count != b.Dimensions.Count) return false;

        foreach (var (id, values) in a.Dimensions)
        {
            if (!b.Dimensions.TryGetValue(id, out var otherValues)) return false;
            if (!values.Select(v => v.Value).SequenceEqual(otherValues.Select(v => v.Value))) return false;
        }

        return true;
    }

    private sealed record RegistrySweepPlan(
        IReadOnlyList<TaskItem> Tasks, bool TasksChanged,
        IReadOnlyList<DayTemplate> DayTemplates, bool DayTemplatesChanged,
        IReadOnlyList<DateOverride> Overrides, bool OverridesChanged,
        IReadOnlyList<Event> Events, bool EventsChanged)
    {
        public bool HasChanges => TasksChanged || DayTemplatesChanged || OverridesChanged || EventsChanged;
    }
}

/// <summary>
/// `manifest.json`'s version is ahead of this binary's <see cref="ManifestCodec.CurrentVersion"/>
/// — an older binary was installed over newer data. Refusing rather than guessing is what keeps a
/// rollback from silently down-migrating already-migrated data.
/// </summary>
public sealed class StoreVersionAheadException(int storedVersion, int currentVersion)
    : Exception(
        $"manifest.json is at version {storedVersion}, ahead of this binary's version {currentVersion}. " +
        "Refusing to start rather than silently down-migrate.")
{
    public int StoredVersion { get; } = storedVersion;
    public int CurrentVersion { get; } = currentVersion;
}
