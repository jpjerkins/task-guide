using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.Infrastructure.Storage;
using Xunit;

namespace TaskGuide.Storage.Tests;

/// <summary>
/// Task 12 (`tests/TEST-INVENTORY.md`, "Sequential · TaskGuide.Storage.Tests"): the store-level
/// mutation rules the earlier lanes made possible. This lane writes no new production
/// abstraction — every test here asserts a rule against the real <see cref="JsonStore"/>.
/// </summary>
public sealed class StoreMutationRulesTests : IDisposable
{
    private readonly string _dataDir = Directory.CreateTempSubdirectory("taskguide-storage-tests-").FullName;

    public void Dispose() => Directory.Delete(_dataDir, recursive: true);

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

    private static TaskItem NewTask(string id, string title) =>
        new(new TaskId(id), title, null, TagSet.Empty, null, null, null, null, DateTimeOffset.UtcNow);

    private static AvailabilityWindow Window(string id, string name, int startHour, int endHour) =>
        new(new WindowId(id), name, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0), TagSet.Empty);

    /// <summary>
    /// `tests/TEST-INVENTORY.md`: "a date materialised mid-day does not re-fire an already-fired
    /// Window". `CONTEXT.md` (712-779): the copy an Override takes of a Day template's Windows
    /// preserves each Window's id, and that id is what keeps the Fire record's (date, windowId)
    /// key matching after a mid-day stamp — without it a freshly-minted id would read as unfired
    /// and push again.
    /// </summary>
    [Fact]
    public async Task A_date_materialised_mid_day_does_not_re_fire_an_already_fired_Window()
    {
        var store = new JsonStore(_dataDir);
        var date = new DateOnly(2026, 8, 31);
        var templateWindow = Window("w_evening", "Evening", 18, 19);
        var template = new DayTemplate(new DayTemplateId("dt_volleyball"), "Volleyball Tuesday", [templateWindow], []);

        await store.MutateAsync(_ => new StoreMutation([new DayTemplatesWrite([template])]), CancellationToken.None);

        // The Window already fired via the Pattern's template, before any Override existed for
        // this date.
        var firedRow = new FireRow(
            templateWindow.Id, FireKind.Window, templateWindow.Name, templateWindow.Start, templateWindow.End,
            DueAt: null, FiredAt: new DateTimeOffset(2026, 8, 31, 18, 5, 0, TimeSpan.Zero), Matched: 1, Carried: null);
        await store.MutateAsync(_ => new StoreMutation([new FiresWrite(new DayFires(date, [firedRow]))]), CancellationToken.None);

        // Mid-day, the template is stamped onto the date. A stamp copies the Windows — a real
        // copy, constructed field-by-field, but preserving the source Window's id.
        var copiedWindow = new AvailabilityWindow(
            templateWindow.Id, templateWindow.Name, templateWindow.Start, templateWindow.End, templateWindow.Tags);
        var stampedOverride = new DateOverride(date, [copiedWindow], new DayTemplateUse(template.Id, template.Name));
        await store.MutateAsync(_ => new StoreMutation([new OverridesWrite([stampedOverride])]), CancellationToken.None);

        var view = store.Read();
        var materialisedWindow = Assert.Single(view.Overrides.Single(o => o.Date == date).Windows);
        Assert.Equal(templateWindow.Id, materialisedWindow.Id);

        var matchingRow = Assert.Single(view.FiresOn(date).Rows, r => r.WindowId == materialisedWindow.Id);
        Assert.True(matchingRow.IsFired);
    }

    /// <summary>
    /// `tests/TEST-INVENTORY.md`: "the use record survives the date becoming a one-off day".
    /// `CONTEXT.md` (712-779): nudging one Window on a stamped date does not un-happen the fact
    /// that shape was reached for — the use record is carried forward unchanged by the caller,
    /// and the store must not silently drop it across a second mutation.
    /// </summary>
    [Fact]
    public async Task The_use_record_survives_the_date_becoming_a_one_off_day()
    {
        var store = new JsonStore(_dataDir);
        var date = new DateOnly(2026, 12, 25);
        var used = new DayTemplateUse(new DayTemplateId("dt_christmas"), "Christmas");
        var stampedWindow = Window("w_family", "Family time", 10, 20);

        await store.MutateAsync(_ => new StoreMutation([new OverridesWrite([new DateOverride(date, [stampedWindow], used)])]), CancellationToken.None);

        // A plain edit to the date's Windows — the use record is carried forward, not cleared.
        var nudgedWindow = stampedWindow with { End = new TimeOnly(21, 0) };
        await store.MutateAsync(
            view => new StoreMutation([new OverridesWrite([.. view.Overrides.Where(o => o.Date != date), new DateOverride(date, [nudgedWindow], used)])]),
            CancellationToken.None);

        var overrideAfterEdit = store.Read().Overrides.Single(o => o.Date == date);
        Assert.Equal(used, overrideAfterEdit.Used);
        Assert.Equal(new TimeOnly(21, 0), Assert.Single(overrideAfterEdit.Windows).End);

        var onDisk = OverrideCodec.Read(File.ReadAllText(Path.Combine(_dataDir, "overrides.json"))).Overrides.Single(o => o.Date == date);
        Assert.Equal(used, onDisk.Used);
    }

    /// <summary>
    /// `tests/TEST-INVENTORY.md`: "re-stamping replaces the use record rather than appending".
    /// `CONTEXT.md` (712-779): the use record is single-valued — re-stamping a date replaces it,
    /// because an accumulating log has no reader.
    /// </summary>
    [Fact]
    public async Task Re_stamping_replaces_the_use_record_rather_than_appending()
    {
        var store = new JsonStore(_dataDir);
        var date = new DateOnly(2026, 12, 25);
        var templateA = new DayTemplateUse(new DayTemplateId("dt_a"), "Template A");
        var templateB = new DayTemplateUse(new DayTemplateId("dt_b"), "Template B");
        var windowA = Window("w_a", "A", 9, 10);
        var windowB = Window("w_b", "B", 11, 12);

        await store.MutateAsync(_ => new StoreMutation([new OverridesWrite([new DateOverride(date, [windowA], templateA)])]), CancellationToken.None);

        // Re-stamping: the caller replaces the date's Override rather than appending a second one.
        await store.MutateAsync(
            view => new StoreMutation([new OverridesWrite([.. view.Overrides.Where(o => o.Date != date), new DateOverride(date, [windowB], templateB)])]),
            CancellationToken.None);

        var overridesForDate = store.Read().Overrides.Where(o => o.Date == date).ToList();
        var only = Assert.Single(overridesForDate);
        Assert.Equal(templateB, only.Used);
        Assert.Equal(windowB, Assert.Single(only.Windows));

        var onDiskForDate = OverrideCodec.Read(File.ReadAllText(Path.Combine(_dataDir, "overrides.json"))).Overrides.Where(o => o.Date == date);
        Assert.Single(onDiskForDate);
    }

    /// <summary>
    /// `tests/TEST-INVENTORY.md`: "promoting a one-off day writes the source date's use record
    /// and does not re-link". `CONTEXT.md` (712-779): promotion copies the shape outward and the
    /// source date keeps its own copy — it does not re-link, so a later edit to the new template
    /// does not reach the source date.
    /// </summary>
    [Fact]
    public async Task Promoting_a_one_off_day_writes_the_source_dates_use_record_and_does_not_re_link()
    {
        var store = new JsonStore(_dataDir);
        var sourceDate = new DateOnly(2026, 12, 25);
        var originalWindow = Window("w_family", "Family time", 10, 20);

        // A one-off day: no use record, nothing stamped.
        await store.MutateAsync(_ => new StoreMutation([new OverridesWrite([new DateOverride(sourceDate, [originalWindow], null)])]), CancellationToken.None);

        var newTemplate = new DayTemplate(new DayTemplateId("dt_christmas"), "Christmas", [originalWindow], []);

        // Promotion: writes the new template (the source date's shape, copied outward) and writes
        // the source date's use record in the same gesture. It does not touch the source date's
        // own Windows — no re-link.
        await store.MutateAsync(view => new StoreMutation(
        [
            new DayTemplatesWrite([.. view.DayTemplates, newTemplate]),
            new OverridesWrite(
            [
                .. view.Overrides.Where(o => o.Date != sourceDate),
                view.Overrides.Single(o => o.Date == sourceDate) with { Used = new DayTemplateUse(newTemplate.Id, newTemplate.Name) },
            ]),
        ]), CancellationToken.None);

        var afterPromotion = store.Read();
        var sourceOverride = afterPromotion.Overrides.Single(o => o.Date == sourceDate);
        Assert.Equal(new DayTemplateUse(newTemplate.Id, newTemplate.Name), sourceOverride.Used);
        Assert.Equal(originalWindow, Assert.Single(sourceOverride.Windows));

        // A later edit to the new template does not reach the source date.
        var editedTemplate = newTemplate with { Windows = [originalWindow with { Name = "Edited elsewhere" }] };
        await store.MutateAsync(
            view => new StoreMutation([new DayTemplatesWrite([.. view.DayTemplates.Where(t => t.Id != newTemplate.Id), editedTemplate])]),
            CancellationToken.None);

        var afterEdit = store.Read();
        var sourceOverrideAfterEdit = afterEdit.Overrides.Single(o => o.Date == sourceDate);
        Assert.Equal("Family time", Assert.Single(sourceOverrideAfterEdit.Windows).Name);
    }

    /// <summary>
    /// `tests/TEST-INVENTORY.md`: "a restore under a running service is invisible, and the next
    /// mutation destroys it" — the one test that documents a failure mode rather than preventing
    /// it. `CONTEXT.md` (1099-1183), "Restoring requires the service stopped": the store is
    /// memory-authoritative, so files restored underneath a live service are invisible, and the
    /// next mutation overwrites the restored bytes from memory. Do not "fix" this — it falls
    /// straight out of memory-authoritative storage and is why #49's restore drill exists.
    /// </summary>
    [Fact]
    public async Task A_restore_under_a_running_service_is_invisible_and_the_next_mutation_destroys_it()
    {
        SeedWholeFixture();
        var store = new JsonStore(_dataDir);
        var before = store.Read().Tasks.Count;
        Assert.True(before > 0);

        // A Backup restore performed on disk while the service is still running — the collection
        // file is overwritten underneath the live store.
        var tasksPath = Path.Combine(_dataDir, "tasks.json");
        var restoredJson = "[]";
        File.WriteAllText(tasksPath, restoredJson);

        // Invisible: the memory-authoritative store still serves the pre-restore state.
        Assert.Equal(before, store.Read().Tasks.Count);

        // The next mutation writes tasks.json from memory, destroying the just-restored bytes.
        var newTask = NewTask("t_01ARZ3NDEKTSV4RRFFQ69G5NEW", "Water the plants");
        await store.MutateAsync(view => new StoreMutation([new TasksWrite([.. view.Tasks, newTask])]), CancellationToken.None);

        Assert.NotEqual(restoredJson, File.ReadAllText(tasksPath));
        Assert.Equal(before + 1, store.Read().Tasks.Count);
    }

    /// <summary>
    /// Beyond-inventory (ruling 2, task-lead directive): <c>JsonStore.cs:373</c> flipped
    /// <c>LastWriteSucceeded</c> to <c>true</c> unconditionally on the success path, unlike the
    /// symmetric failure path a few lines above which is guarded by <c>attemptedWrite</c>. An
    /// empty <c>OrderedWrites</c> list — no caller does this today, but nothing prevented it —
    /// would flip `null` to `true` without a byte reaching disk: a false healthy, which
    /// `IStore.LastWriteSucceeded`'s own doc says must never happen.
    /// </summary>
    [Fact]
    public async Task A_mutation_with_no_writes_leaves_LastWriteSucceeded_untouched()
    {
        var store = new JsonStore(_dataDir);
        Assert.Null(store.LastWriteSucceeded);

        await store.MutateAsync(_ => new StoreMutation([]), CancellationToken.None);

        Assert.Null(store.LastWriteSucceeded);
    }

    /// <summary>
    /// `tests/TEST-INVENTORY.md`: "deleting an `Unused` template corrupts no record". Rebuilt
    /// here from `DayTemplateLifecycleTests` (routed by #52's final triage) — the Domain version
    /// asserted only that `List&lt;T&gt;.Remove` works and that a local array the test itself
    /// built was unchanged, both true for any implementation. This version deletes the template
    /// through a real mutation against the real store and re-reads: every Override's Windows must
    /// survive byte-identical, since an Override always holds a copy rather than a reference.
    /// </summary>
    [Fact]
    public async Task Deleting_an_Unused_template_corrupts_no_record()
    {
        var store = new JsonStore(_dataDir);
        var unused = new DayTemplateId("dt_volleyball");
        var keep = new DayTemplateId("dt_workday");
        var sharedWindow = Window("w_evening", "Evening", 18, 19);

        await store.MutateAsync(_ => new StoreMutation(
        [
            new DayTemplatesWrite(
            [
                new DayTemplate(unused, "Volleyball Tuesday", [sharedWindow], []),
                new DayTemplate(keep, "Workday", [], []),
            ]),
        ]), CancellationToken.None);

        var stampedFromUnused = new DateOverride(new DateOnly(2026, 8, 15), [sharedWindow], new DayTemplateUse(unused, "Volleyball Tuesday"));
        var oneOffDay = new DateOverride(new DateOnly(2026, 8, 20), [sharedWindow], null);
        await store.MutateAsync(_ => new StoreMutation([new OverridesWrite([stampedFromUnused, oneOffDay])]), CancellationToken.None);

        var windowsBefore = store.Read().Overrides.ToDictionary(o => o.Date, o => o.Windows);

        // "Deleting" is dropping the id from the caller's templates list and writing it back —
        // there is no re-link to sever, since every Override already holds its own copy.
        await store.MutateAsync(
            view => new StoreMutation([new DayTemplatesWrite([.. view.DayTemplates.Where(t => t.Id != unused)])]),
            CancellationToken.None);

        var afterDelete = store.Read();
        Assert.DoesNotContain(afterDelete.DayTemplates, t => t.Id == unused);
        foreach (var (date, windowsBeforeDelete) in windowsBefore)
        {
            Assert.Equal(windowsBeforeDelete, afterDelete.Overrides.Single(o => o.Date == date).Windows);
        }
    }
}
