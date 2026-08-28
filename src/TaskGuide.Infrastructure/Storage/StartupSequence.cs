using System.Text.Json;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;

namespace TaskGuide.Infrastructure.Storage;

/// <summary>
/// <b>assert → snapshot → migrate → sweep.</b> Composes 8a's <see cref="ManifestCodec"/> and
/// <see cref="SnapshotWriter"/> with the Dimension registry (#21) and <see cref="RegistrySweep"/>
/// into the one orchestrated startup phase ADR-0001 says memory-authoritative depends on.
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
    Func<string, CancellationToken, Task> signalRegistryCollision)
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
    /// nothing at all.
    /// </summary>
    public async Task SweepRegistryAsync(CancellationToken cancellationToken)
    {
        var plan = ComputeSweepPlan(store.Read());
        if (!plan.HasChanges) return;

        var writes = new List<object>();
        if (plan.TasksChanged) writes.Add(new TasksWrite(plan.Tasks));
        if (plan.DayTemplatesChanged) writes.Add(new DayTemplatesWrite(plan.DayTemplates));
        if (plan.OverridesChanged) writes.Add(new OverridesWrite(plan.Overrides));
        if (plan.EventsChanged) writes.Add(new EventsWrite(plan.Events));

        await store.MutateAsync(_ => new StoreMutation(writes), cancellationToken);
    }

    /// <summary>
    /// The composition root's entry point: assert → snapshot → migrate → sweep. A registry
    /// collision signals outbound (awaited) before the exception propagates and nothing else
    /// runs. Otherwise the snapshot runs at most once, and only when a migration or a registry
    /// sweep is actually about to write.
    /// </summary>
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

        // Reading version-ahead here, before any snapshot decision, is what makes that refusal
        // write nothing: PlanMigration throws before SnapshotAsync is ever considered.
        var (storedVersion, pendingMigrations) = PlanMigration();
        var sweepPlan = ComputeSweepPlan(store.Read());

        var willWrite = pendingMigrations.Count > 0 || sweepPlan.HasChanges;
        if (willWrite)
        {
            await SnapshotAsync(cancellationToken);
        }

        if (pendingMigrations.Count > 0 || storedVersion is null)
        {
            await MigrateAsync(cancellationToken);
        }

        if (sweepPlan.HasChanges)
        {
            await SweepRegistryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Reads `manifest.json` (absent reads as "fresh, nothing to migrate") and walks
    /// <paramref name="migrations"/> forward from its version, one N→N+1 step at a time, for as
    /// long as a step starting at the current cursor exists. Throws
    /// <see cref="StoreVersionAheadException"/> immediately — no steps attempted — when the
    /// stored version is ahead of this binary's <see cref="ManifestCodec.CurrentVersion"/>: a
    /// rollback must not silently down-migrate. The walk itself is not bounded by
    /// <see cref="ManifestCodec.CurrentVersion"/> — it stops when <paramref name="migrations"/>
    /// has nothing left to offer, which is what lets a test supply its own short list and land
    /// wherever that list says, without needing to fake the constant (see
    /// <see cref="StoreMigrations.Ordered"/>, empty today, for why production never notices).
    /// </summary>
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
            pending.Add(step);
            cursor = step.To;
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
