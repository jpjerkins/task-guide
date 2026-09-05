using OneOf;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;

namespace TaskGuide.Infrastructure.Storage;

/// <summary>
/// ADR-0009's plan phase: assert → plan migration → plan seed → plan sweep, in that order, over a
/// bootstrap view that can only be read — <see cref="IStoreReader"/>, not <see cref="IStore"/>, is
/// the enforcement: a planner cannot be handed something that writes, so it cannot write by
/// construction, not merely by convention.
/// </summary>
/// <remarks>
/// The seed and the sweep are planned together, in that order, over the <em>same</em>
/// collections: the sweep must see the seed's own Day template, or its
/// <see cref="DayTemplatesWrite"/> would carry the pre-seed list and erase what the seed just
/// added when both land through one <see cref="StartupPlan.OrderedWrites"/>. Planning the sweep
/// from the raw bootstrap view instead of the post-seed collections is exactly the bug this
/// ordering avoids (see the mutation drill in <c>StartupBootstrapTests</c>).
/// </remarks>
public sealed class StartupPlanner(
    string dataDir,
    DimensionRegistry registry,
    IReadOnlyList<StoreMigration> migrations,
    IIdMinter idMinter) : IStartupPlanner
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

    public OneOf<StartupPlan, StartupRefusal> Plan(IStoreReader bootstrap)
    {
        try
        {
            registry.AssertNoDuplicateValues();
        }
        catch (DuplicateDimensionValueException ex)
        {
            return new StartupRefusal(new RegistryCollision(ex.Message));
        }

        var migrationResult = PlanMigration();
        if (migrationResult.IsT1) return migrationResult.AsT1;
        var (pendingMigrations, manifestVersion) = migrationResult.AsT0;

        var view = bootstrap.Read();
        var (seedTemplate, seedDayTemplates, seedPattern) = PlanSeed(view);

        var sweepPlan = ComputeSweepPlan(
            view.Tasks,
            seedDayTemplates,
            view.Overrides,
            view.Events);

        var writes = new List<object>();
        if (sweepPlan.TasksChanged) writes.Add(new TasksWrite(sweepPlan.Tasks));
        if (sweepPlan.DayTemplatesChanged || seedTemplate is not null) writes.Add(new DayTemplatesWrite(sweepPlan.DayTemplates));
        if (seedPattern is not null) writes.Add(new PatternsWrite(seedPattern));
        if (sweepPlan.OverridesChanged) writes.Add(new OverridesWrite(sweepPlan.Overrides));
        if (sweepPlan.EventsChanged) writes.Add(new EventsWrite(sweepPlan.Events));

        var orderedWrites = writes.Count == 0
            ? []
            : (IReadOnlyList<StoreMutation>)[new StoreMutation(writes)];

        var willWrite = pendingMigrations.Count > 0 || sweepPlan.HasChanges;
        var filesToSnapshot = willWrite
            ? CollectionFileNames.Where(name => File.Exists(Path.Combine(dataDir, name))).ToArray()
            : [];

        return new StartupPlan(
            filesToSnapshot,
            pendingMigrations.Select(m => m.Apply).ToArray(),
            manifestVersion,
            orderedWrites);
    }

    /// <summary>
    /// The vanilla default weekly Pattern: seven days of one plain Day template, carrying no
    /// Availability Windows and no Event prototypes — any authored Window or prototype would
    /// assert a schedule opinion a brand-new store has no basis for. Planned only when the loaded
    /// <see cref="PatternBook"/> has <em>no Patterns</em>, not merely when `patterns.json` is
    /// absent: a present-but-empty `patterns.json` crashes <see cref="PatternBook.Active"/> exactly
    /// the same way an absent file does. Builds the Day template list from the view's own list
    /// plus the new template — never from an empty list, so a store with templates but no Pattern
    /// does not have them erased.
    /// </summary>
    private (DayTemplate? Template, IReadOnlyList<DayTemplate> DayTemplates, PatternBook? Pattern) PlanSeed(IStoreView view)
    {
        if (view.Patterns.Patterns.Count > 0) return (null, view.DayTemplates, null);

        var template = new DayTemplate(idMinter.NextDayTemplateId(), "Ordinary day", [], []);
        var days = Enumerable.Repeat(template.Id, 7).ToArray();
        var pattern = new Pattern(idMinter.NextPatternId(), "Default", days);
        var book = new PatternBook(pattern.Id, [pattern]);

        return (template, [.. view.DayTemplates, template], book);
    }

    /// <summary>
    /// Reads `manifest.json` (absent reads as "fresh, nothing to migrate") and walks
    /// <see cref="migrations"/> forward from its version, one N→N+1 step at a time, for as long as
    /// a step starting at the current cursor exists. Refuses — no steps attempted — when the
    /// stored version already on disk is ahead of this binary's <see cref="ManifestCodec.CurrentVersion"/>:
    /// a rollback must not silently down-migrate.
    /// </summary>
    /// <remarks>
    /// The walk's own endpoint is not bounded by <see cref="ManifestCodec.CurrentVersion"/> while
    /// it runs — it stops when <see cref="migrations"/> has nothing left to offer for the current
    /// cursor, which is what lets a test supply its own short list and land wherever that list
    /// says, without needing to fake the constant (see <see cref="StoreMigrations.Ordered"/>,
    /// empty today, for why production never notices). Two guardrails still apply to whatever the
    /// walk finds, both cheap because a well-formed N→N+1 list already satisfies them and neither
    /// reintroduces the "can't fake the constant" problem:
    /// <list type="bullet">
    /// <item>Each step moves the cursor <em>strictly forward</em>. This is not checked here: it is
    /// an invariant of <see cref="StoreMigration"/>, enforced at construction per ADR-0009, so the
    /// cycle that would spin this loop forever cannot be built — not in production, and not in a
    /// test's fake list either.</item>
    /// <item>The walk's landing version must not exceed <see cref="ManifestCodec.CurrentVersion"/>.
    /// Without this, a walk that outruns what this binary can run would leave a `manifest.json`
    /// this very binary would refuse to start on next boot.</item>
    /// </list>
    /// A gap the walk cannot cross (no step registered for some cursor it reaches, short of
    /// <see cref="ManifestCodec.CurrentVersion"/>) is not treated as an error — the walk simply
    /// stops there, silently, which only matters once a real gap can exist; today's empty
    /// <see cref="StoreMigrations.Ordered"/> never produces one.
    /// </remarks>
    private OneOf<(IReadOnlyList<StoreMigration> Pending, int? ManifestVersion), StartupRefusal> PlanMigration()
    {
        var manifestPath = Path.Combine(dataDir, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            // A missing manifest.json is a fresh `/data`, never migrated by any binary — there is
            // nothing to walk, but the file itself still needs to exist so a later startup has a
            // version to read.
            return (Array.Empty<StoreMigration>(), ManifestCodec.CurrentVersion);
        }

        var version = ManifestCodec.Read(File.ReadAllText(manifestPath));

        if (version > ManifestCodec.CurrentVersion)
        {
            return new StartupRefusal(new StoreVersionAhead(version, ManifestCodec.CurrentVersion));
        }

        var pending = new List<StoreMigration>();
        var cursor = version;
        while (migrations.FirstOrDefault(m => m.From == cursor) is { } step)
        {
            // No monotonicity check here: `step.To > step.From` is an invariant of StoreMigration,
            // enforced at construction (ADR-0009), and the walk selects on `m.From == cursor`, so
            // `step.To > cursor` holds for every step this loop can ever see. The cycle that would
            // hang this loop cannot be built.
            pending.Add(step);
            cursor = step.To;
        }

        if (pending.Count == 0)
        {
            // Already at some version, nothing pending: manifest.json exists and stays as it is.
            return (pending, null);
        }

        if (cursor > ManifestCodec.CurrentVersion)
        {
            return new StartupRefusal(new StoreVersionAhead(cursor, ManifestCodec.CurrentVersion));
        }

        // The walk's own endpoint, not the constant CurrentVersion: `migrations` is supplied at
        // construction (StoreMigrations.Ordered in production, empty today) and the manifest
        // reflects wherever the ordered steps actually landed.
        return (pending, cursor);
    }

    /// <summary>
    /// The promote/demote sweep, computed over explicit collections rather than an
    /// <see cref="IStoreView"/> — the caller passes the <em>post-seed</em> Day template list, not
    /// the bootstrap view's own, so a sweep that runs after a seed sees the template the seed just
    /// added instead of erasing it.
    /// </summary>
    /// <remarks>
    /// <b>Known residual, not fixed here — ADR-0009's Consequences records it explicitly:</b> this
    /// still plans from the bootstrap view, which is exactly as stale as today's code once a real
    /// migration step writes data the sweep should see. Closing that needs either a store-reload
    /// API or migrations that go through the store, both out of this ticket's lane; #53's
    /// store-reload question is what closes it.
    /// </remarks>
    private RegistrySweepPlan ComputeSweepPlan(
        IReadOnlyList<TaskItem> tasks,
        IReadOnlyList<DayTemplate> dayTemplates,
        IReadOnlyList<DateOverride> overrides,
        IReadOnlyList<Event> events)
    {
        var (sweptTasks, tasksChanged) = SweepTasks(tasks);
        var (sweptTemplates, templatesChanged) = SweepDayTemplates(dayTemplates);
        var (sweptOverrides, overridesChanged) = SweepOverrides(overrides);
        var (sweptEvents, eventsChanged) = SweepEvents(events);

        return new RegistrySweepPlan(
            sweptTasks, tasksChanged,
            sweptTemplates, templatesChanged,
            sweptOverrides, overridesChanged,
            sweptEvents, eventsChanged);

        (IReadOnlyList<TaskItem>, bool) SweepTasks(IReadOnlyList<TaskItem> input)
        {
            var changed = false;
            var swept = input.Select(t =>
            {
                var newTags = RegistrySweep.Sweep(t.Tags, registry);
                if (!t.Tags.Equals(newTags)) changed = true;
                return t with { Tags = newTags };
            }).ToArray();

            return (swept, changed);
        }

        (IReadOnlyList<DayTemplate>, bool) SweepDayTemplates(IReadOnlyList<DayTemplate> input)
        {
            var changed = false;
            var swept = input.Select(t =>
            {
                var windows = t.Windows.Select(w =>
                {
                    var newTags = RegistrySweep.Sweep(w.Tags, registry);
                    if (!w.Tags.Equals(newTags)) changed = true;
                    return w with { Tags = newTags };
                }).ToArray();

                var prototypes = t.EventPrototypes.Select(p =>
                {
                    var newTags = RegistrySweep.Sweep(p.Tags, registry);
                    if (!p.Tags.Equals(newTags)) changed = true;
                    return p with { Tags = newTags };
                }).ToArray();

                return t with { Windows = windows, EventPrototypes = prototypes };
            }).ToArray();

            return (swept, changed);
        }

        (IReadOnlyList<DateOverride>, bool) SweepOverrides(IReadOnlyList<DateOverride> input)
        {
            var changed = false;
            var swept = input.Select(o =>
            {
                var windows = o.Windows.Select(w =>
                {
                    var newTags = RegistrySweep.Sweep(w.Tags, registry);
                    if (!w.Tags.Equals(newTags)) changed = true;
                    return w with { Tags = newTags };
                }).ToArray();

                return o with { Windows = windows };
            }).ToArray();

            return (swept, changed);
        }

        (IReadOnlyList<Event>, bool) SweepEvents(IReadOnlyList<Event> input)
        {
            var changed = false;
            var swept = input.Select(e =>
            {
                var newTags = RegistrySweep.Sweep(e.Tags, registry);
                if (!e.Tags.Equals(newTags)) changed = true;
                return e with { Tags = newTags };
            }).ToArray();

            return (swept, changed);
        }
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
