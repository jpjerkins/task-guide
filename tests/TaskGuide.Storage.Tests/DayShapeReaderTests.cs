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
/// Task 11 (`tests/TEST-INVENTORY.md`, "Sequential · TaskGuide.Storage.Tests"): reading a
/// DayShape from the in-memory store without materialising one.
/// </summary>
public sealed class DayShapeReaderTests
{
    [Fact]
    public void A_date_with_no_Override_takes_the_active_Patterns_template_for_its_weekday()
    {
        var monday = new DateOnly(2026, 8, 31);
        var mondayWindow = Window("w_monday", "Monday");
        var sundayWindow = Window("w_sunday", "Sunday");
        var mondayTemplate = Template("dt_monday", "Monday template", [mondayWindow]);
        var sundayTemplate = Template("dt_sunday", "Sunday template", [sundayWindow]);
        var store = Store(dayTemplates: [sundayTemplate, mondayTemplate], patterns: PatternBook(
            active: "p_active",
            sunday: sundayTemplate.Id,
            monday: mondayTemplate.Id),
            overrides: [new DateOverride(monday.AddDays(1), [Window("w_wrong_date", "Wrong date")], null)]);

        var shape = new DayShapeReader(store).For(monday);

        Assert.False(shape.IsOverridden);
        Assert.Equal(monday, shape.Date);
        var window = Assert.Single(shape.Windows);
        Assert.Equal(mondayWindow, window);
    }

    [Fact]
    public void A_Pattern_naming_an_absent_Day_template_throws_naming_the_template_the_Pattern_and_the_date()
    {
        var monday = new DateOnly(2026, 8, 31);
        var store = Store(
            dayTemplates: [],
            patterns: PatternBook("p_active", sunday: new DayTemplateId("dt_sunday"), monday: new DayTemplateId("dt_monday")));

        var ex = Assert.Throws<InvalidOperationException>(() => new DayShapeReader(store).For(monday));

        Assert.Contains("dt_monday", ex.Message);
        Assert.Contains("p_active", ex.Message);
        Assert.Contains("2026-08-31", ex.Message);
    }

    [Fact]
    public void A_date_with_an_Override_takes_the_Overrides_Windows_and_reads_IsOverridden()
    {
        var monday = new DateOnly(2026, 8, 31);
        var patternWindow = Window("w_pattern", "Pattern");
        var overrideWindow = Window("w_override", "Override", startHour: 13, endHour: 14);
        var mondayTemplate = Template("dt_monday", "Monday template", [patternWindow]);
        var store = Store(
            dayTemplates: [mondayTemplate],
            patterns: PatternBook("p_active", sunday: mondayTemplate.Id, monday: mondayTemplate.Id),
            overrides: [new DateOverride(monday, [overrideWindow], null)]);

        var shape = new DayShapeReader(store).For(monday);

        Assert.True(shape.IsOverridden);
        var window = Assert.Single(shape.Windows);
        Assert.Equal(overrideWindow, window);
        Assert.DoesNotContain(shape.Windows, w => w.Id == patternWindow.Id);
    }

    [Fact]
    public void An_Override_with_zero_Windows_is_a_shape_not_an_absence_IsOverridden_is_true_and_the_Patterns_Windows_do_not_leak_through()
    {
        var monday = new DateOnly(2026, 8, 31);
        var patternWindow = Window("w_pattern", "Pattern");
        var mondayTemplate = Template("dt_monday", "Monday template", [patternWindow]);
        var store = Store(
            dayTemplates: [mondayTemplate],
            patterns: PatternBook("p_active", sunday: mondayTemplate.Id, monday: mondayTemplate.Id),
            overrides: [new DateOverride(monday, [], null)]);

        var shape = new DayShapeReader(store).For(monday);

        Assert.True(shape.IsOverridden);
        Assert.Empty(shape.Windows);
    }

    [Fact]
    public void A_dated_Event_on_the_date_appears_in_the_shape()
    {
        var date = new DateOnly(2026, 8, 31);
        var eventOnDate = Event("evt_band", date, "Band");
        var eventOnOtherDate = Event("evt_dentist", date.AddDays(1), "Dentist");
        var template = Template("dt_monday", "Monday template");
        var store = Store(
            dayTemplates: [template],
            patterns: PatternBook("p_active", sunday: template.Id, monday: template.Id),
            events: [eventOnOtherDate, eventOnDate]);

        var shape = new DayShapeReader(store).For(date);

        var actual = Assert.Single(shape.Events);
        Assert.Equal(eventOnDate, actual);
    }

    [Fact]
    public void A_recurring_instance_from_the_weekdays_Event_prototype_appears_in_the_shape()
    {
        var date = new DateOnly(2026, 8, 31);
        var prototype = Prototype("ep_karate", "Karate");
        var template = Template("dt_monday", "Monday template", prototypes: [prototype]);
        var store = Store(
            dayTemplates: [template],
            patterns: PatternBook("p_active", sunday: template.Id, monday: template.Id));

        var shape = new DayShapeReader(store).For(date);

        var actual = Assert.Single(shape.Events);
        Assert.Equal(date, actual.Date);
        Assert.Equal("Karate", actual.Name);
        Assert.Equal(prototype.Start, actual.Start);
        Assert.Equal(prototype.End, actual.End);
        Assert.Equal(prototype.Tags, actual.Tags);
        Assert.Equal(prototype.AbsenceNotice, actual.AbsenceNotice);
    }

    [Fact]
    public void A_deleted_instances_Event_exception_drops_it()
    {
        var date = new DateOnly(2026, 8, 31);
        var prototype = Prototype("ep_karate", "Karate");
        var template = Template("dt_monday", "Monday template", prototypes: [prototype]);
        var store = Store(
            dayTemplates: [template],
            patterns: PatternBook("p_active", sunday: template.Id, monday: template.Id),
            eventExceptions: [new EventException(date, prototype.Id, Deleted: true, null, null, null)]);

        var shape = new DayShapeReader(store).For(date);

        Assert.Empty(shape.Events);
    }

    [Fact]
    public void An_edited_instances_Event_exception_replaces_its_name_and_span_leaving_the_prototype_untouched()
    {
        var date = new DateOnly(2026, 8, 31);
        var prototype = Prototype("ep_karate", "Karate", startHour: 17, endHour: 18);
        var template = Template("dt_monday", "Monday template", prototypes: [prototype]);
        var store = Store(
            dayTemplates: [template],
            patterns: PatternBook("p_active", sunday: template.Id, monday: template.Id),
            eventExceptions: [new EventException(date, prototype.Id, Deleted: false, "Karate late", new TimeOnly(19, 0), new TimeOnly(20, 0))]);

        var shape = new DayShapeReader(store).For(date);

        var actual = Assert.Single(shape.Events);
        Assert.Equal("Karate late", actual.Name);
        Assert.Equal(new TimeOnly(19, 0), actual.Start);
        Assert.Equal(new TimeOnly(20, 0), actual.End);
        Assert.Equal("Karate", prototype.Name);
        Assert.Equal(new TimeOnly(17, 0), prototype.Start);
        Assert.Equal(new TimeOnly(18, 0), prototype.End);
    }

    [Fact]
    public void An_Event_exception_for_a_different_prototype_on_the_same_date_changes_nothing()
    {
        var date = new DateOnly(2026, 8, 31);
        var prototype = Prototype("ep_karate", "Karate", startHour: 17, endHour: 18);
        var template = Template("dt_monday", "Monday template", prototypes: [prototype]);
        var store = Store(
            dayTemplates: [template],
            patterns: PatternBook("p_active", sunday: template.Id, monday: template.Id),
            eventExceptions:
            [
                new EventException(date, new EventPrototypeId("ep_piano"), Deleted: false, "Piano", new TimeOnly(19, 0), new TimeOnly(20, 0)),
                new EventException(date.AddDays(1), prototype.Id, Deleted: false, "Karate tomorrow", new TimeOnly(19, 0), new TimeOnly(20, 0)),
            ]);

        var shape = new DayShapeReader(store).For(date);

        var actual = Assert.Single(shape.Events);
        Assert.Equal("Karate", actual.Name);
        Assert.Equal(new TimeOnly(17, 0), actual.Start);
        Assert.Equal(new TimeOnly(18, 0), actual.End);
    }

    [Fact]
    public void Reading_a_days_shape_writes_nothing_no_Override_is_materialised_and_MutateAsync_is_never_called()
    {
        var date = new DateOnly(2026, 8, 31);
        var template = Template("dt_monday", "Monday template", [Window("w_monday", "Monday")]);
        var store = Store(
            dayTemplates: [template],
            patterns: PatternBook("p_active", sunday: template.Id, monday: template.Id));

        var shape = new DayShapeReader(store).For(date);

        Assert.False(shape.IsOverridden);
        Assert.Empty(store.Read().Overrides);
    }

    [Fact]
    public void A_recurring_instances_Event_id_is_the_same_on_two_reads_of_the_same_date()
    {
        var date = new DateOnly(2026, 8, 31);
        var prototype = Prototype("ep_karate", "Karate");
        var template = Template("dt_monday", "Monday template", prototypes: [prototype]);
        var store = Store(
            dayTemplates: [template],
            patterns: PatternBook("p_active", sunday: template.Id, monday: template.Id));
        var reader = new DayShapeReader(store);

        var first = Assert.Single(reader.For(date).Events);
        var second = Assert.Single(reader.For(date).Events);

        Assert.Equal(first.Id, second.Id);
    }

    private static AvailabilityWindow Window(string id, string name, int startHour = 9, int endHour = 10) =>
        new(new WindowId(id), name, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0), TagSet.Empty);

    private static Event Event(string id, DateOnly date, string name, int startHour = 18, int endHour = 19) =>
        new(new EventId(id), date, name, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0), TagSet.Empty, null);

    private static EventPrototype Prototype(string id, string name, int startHour = 18, int endHour = 19) =>
        new(new EventPrototypeId(id), name, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0), TagSet.Empty, null);

    private static DayTemplate Template(
        string id,
        string name,
        IReadOnlyList<AvailabilityWindow>? windows = null,
        IReadOnlyList<EventPrototype>? prototypes = null) =>
        new(new DayTemplateId(id), name, windows ?? [], prototypes ?? []);

    private static PatternBook PatternBook(string active, DayTemplateId sunday, DayTemplateId monday)
    {
        var sundayThroughSaturday = new[]
        {
            sunday,
            monday,
            new DayTemplateId("dt_tuesday"),
            new DayTemplateId("dt_wednesday"),
            new DayTemplateId("dt_thursday"),
            new DayTemplateId("dt_friday"),
            new DayTemplateId("dt_saturday"),
        };

        return new PatternBook(new PatternId(active), [new Pattern(new PatternId(active), "Active", sundayThroughSaturday)]);
    }

    private static SpyStore Store(
        IReadOnlyList<DayTemplate>? dayTemplates = null,
        PatternBook? patterns = null,
        IReadOnlyList<DateOverride>? overrides = null,
        IReadOnlyList<Event>? events = null,
        IReadOnlyList<EventException>? eventExceptions = null) =>
        new(new MemoryView(
            dayTemplates ?? [],
            patterns ?? PatternBook("p_active", new DayTemplateId("dt_sunday"), new DayTemplateId("dt_monday")),
            overrides ?? [],
            events ?? [],
            eventExceptions ?? []));

    private sealed class SpyStore(IStoreView view) : IStore
    {
        public IStoreView Read() => view;

        public Task MutateAsync(Func<IStoreView, StoreMutation> mutation, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("DayShapeReader must not write.");

        public bool? LastWriteSucceeded => null;
    }

    private sealed class MemoryView(
        IReadOnlyList<DayTemplate> dayTemplates,
        PatternBook patterns,
        IReadOnlyList<DateOverride> overrides,
        IReadOnlyList<Event> events,
        IReadOnlyList<EventException> eventExceptions) : IStoreView
    {
        public IReadOnlyList<TaskItem> Tasks => [];
        public CompletionLog CompletionsFor(TaskId task) => CompletionLog.Empty(task);
        public IReadOnlyList<DerivedCompletionEntry> DerivedCompletions => [];
        public IReadOnlyList<DayTemplate> DayTemplates => dayTemplates;
        public PatternBook Patterns => patterns;
        public IReadOnlyList<DateOverride> Overrides => overrides;
        public IReadOnlyList<Event> Events => events;
        public IReadOnlyList<EventException> EventExceptions => eventExceptions;
        public DayFires FiresOn(DateOnly date) => new(date, []);
    }
}
