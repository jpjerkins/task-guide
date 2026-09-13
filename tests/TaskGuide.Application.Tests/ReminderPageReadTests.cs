using TaskGuide.Application.Firing;
using TaskGuide.Application.Reminders;
using TaskGuide.Application.Rules;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Notifications;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.Domain.Time;
using TaskGuide.TestSupport;
using Xunit;

namespace TaskGuide.Application.Tests;

public sealed class ReminderPageReadTests
{
    private static readonly DayBoundary Boundary = new(TimeZoneInfo.Utc);
    private static readonly ClockTimeResolution Resolution = new(Boundary);
    private static readonly DateOnly Today = new(2026, 9, 5);
    private static readonly StaleThresholds Thresholds = new(TimeSpan.FromDays(60), 3);

    [Fact]
    public async Task All_matches_are_returned_ranked_not_the_pushs_shortlist_of_three()
    {
        var window = Window("w_morning", 9, 10);
        var shapes = Shapes(window);
        var now = Resolution.Resolve(Today, new TimeOnly(9, 0));
        var store = new FakeStore(new FakeStoreViewBuilder()
            .WithTasks([Task("t1"), Task("t2"), Task("t3"), Task("t4")])
            .Build());

        var page = await Page(store, shapes, now, "w_morning");

        Assert.Equal(4, page.Matches.Count);
    }

    [Fact]
    public async Task Durations_ceiling_is_re_derived_from_the_time_actually_remaining_and_floors_at_the_smallest_bucket_once_the_span_is_spent()
    {
        var window = Window("w_morning", 9, 10);
        var shapes = Shapes(window);
        var now = Resolution.Resolve(Today, new TimeOnly(9, 50)); // 10 minutes left in a 60-minute Window
        var task10 = Task("t_ten", duration: "10");
        var task60 = Task("t_sixty", duration: "60");
        var store = new FakeStore(new FakeStoreViewBuilder().WithTasks([task60, task10]).Build());

        var page = await Page(store, shapes, now, "w_morning");

        Assert.Equal([task10], page.Matches);
    }

    [Fact]
    public async Task The_page_level_liveness_gate_is_one_predicate_past_the_Reminders_Day_boundary_the_page_is_not_live_and_carries_This_reminder_was_for_yesterday()
    {
        var window = Window("w_evening", 22, 23);
        var shapes = Shapes(window);
        var fired = new FireRow(window.Id, FireKind.Window, window.Name, window.Start, window.End,
            null, Resolution.Resolve(Today, new TimeOnly(22, 0)), 0, null);
        var store = new FakeStore(new FakeStoreViewBuilder().WithFires(Today, new DayFires(Today, [fired])).Build());
        var now = new DateTimeOffset(2026, 9, 6, 0, 5, 0, TimeSpan.Zero);

        var page = await Page(store, shapes, now, "w_evening");

        Assert.False(page.IsLive);
        Assert.Equal("This reminder was for yesterday", page.StaleLine);
    }

    [Fact]
    public async Task The_Snooze_suppression_line_comes_from_the_same_rule_the_Snooze_command_refuses_with_Snooze_ends_at_midnight_inside_the_day_This_reminder_was_for_yesterday_past_it()
    {
        var lateWindow = Window("w_late", 23, 45, 23, 55);
        var lateFired = new FireRow(lateWindow.Id, FireKind.Window, lateWindow.Name, lateWindow.Start, lateWindow.End,
            null, new DateTimeOffset(2026, 9, 5, 23, 45, 0, TimeSpan.Zero), 0, null);
        var insideDayStore = new FakeStore(new FakeStoreViewBuilder().WithFires(Today, new DayFires(Today, [lateFired])).Build());
        var insideDayPage = await Page(insideDayStore, Shapes(lateWindow), new DateTimeOffset(2026, 9, 5, 23, 56, 0, TimeSpan.Zero), "w_late");
        Assert.Equal("Snooze ends at midnight", Assert.IsType<WindowContext>(insideDayPage.Context.Value).SnoozeSuppression);

        var eveningWindow = Window("w_evening", 22, 23);
        var eveningFired = new FireRow(eveningWindow.Id, FireKind.Window, eveningWindow.Name, eveningWindow.Start, eveningWindow.End,
            null, new DateTimeOffset(2026, 9, 5, 22, 0, 0, TimeSpan.Zero), 0, null);
        var pastBoundaryStore = new FakeStore(new FakeStoreViewBuilder().WithFires(Today, new DayFires(Today, [eveningFired])).Build());
        var pastBoundaryPage = await Page(pastBoundaryStore, Shapes(eveningWindow), new DateTimeOffset(2026, 9, 6, 0, 5, 0, TimeSpan.Zero), "w_evening");
        Assert.Equal("This reminder was for yesterday", Assert.IsType<WindowContext>(pastBoundaryPage.Context.Value).SnoozeSuppression);
    }

    [Fact]
    public async Task The_Snooze_interval_is_clamp_25_5_30_computed_server_side()
    {
        var now = Resolution.Resolve(Today, new TimeOnly(9, 0));

        var shortWindow = Window("w_short", 9, 0, 9, 10);
        var shortStore = new FakeStore(new FakeStoreViewBuilder().Build());
        var shortPage = await Page(shortStore, Shapes(shortWindow), now, "w_short");
        Assert.Equal(5, Assert.IsType<WindowContext>(shortPage.Context.Value).SnoozeIntervalMinutes);

        var longWindow = Window("w_long", 9, 13);
        var longStore = new FakeStore(new FakeStoreViewBuilder().Build());
        var longPage = await Page(longStore, Shapes(longWindow), now, "w_long");
        Assert.Equal(30, Assert.IsType<WindowContext>(longPage.Context.Value).SnoozeIntervalMinutes);
    }

    [Fact]
    public async Task A_fallback_page_has_no_Window_behind_it_and_so_carries_no_Snooze()
    {
        var now = Resolution.Resolve(Today, new TimeOnly(11, 0));
        var carrier = new Event(new EventId("evt_trip"), Today, "Family trip", new TimeOnly(9, 0), new TimeOnly(10, 0), TagSet.Empty, null);
        var fired = new FireRow(null, FireKind.Fallback, null, null, null, null, now, null, carrier.Id);
        var store = new FakeStore(new FakeStoreViewBuilder()
            .WithEvents([carrier])
            .WithFires(Today, new DayFires(Today, [fired]))
            .Build());

        var page = await Page(store, new FakeDayShapeReader(), now, "fallback");

        var fallback = Assert.IsType<FallbackContext>(page.Context.Value);
        Assert.Equal("Family trip", fallback.EventName);
    }

    [Fact]
    public async Task An_empty_Window_page_names_no_fire_when_none_fired_and_names_the_fire_when_an_unconditional_fire_an_empty_Snooze_re_fire_or_a_fallback_push_genuinely_pushed()
    {
        var window = Window("w_morning", 9, 10);
        var now = Resolution.Resolve(Today, new TimeOnly(9, 0));

        var nothingFired = await Page(new FakeStore(new FakeStoreViewBuilder().Build()), Shapes(window), now, "w_morning");
        Assert.Null(nothingFired.FiredAs);

        var unconditional = new FireRow(window.Id, FireKind.Unconditional, window.Name, window.Start, window.End, null, now, 0, null);
        var unconditionalPage = await Page(
            new FakeStore(new FakeStoreViewBuilder().WithFires(Today, new DayFires(Today, [unconditional])).Build()),
            Shapes(window), now, "w_morning");
        Assert.Equal(FireKind.Unconditional, unconditionalPage.FiredAs);

        var emptySnooze = new FireRow(window.Id, FireKind.Snooze, window.Name, window.Start, window.End, null, now, 0, null);
        var snoozePage = await Page(
            new FakeStore(new FakeStoreViewBuilder().WithFires(Today, new DayFires(Today, [emptySnooze])).Build()),
            Shapes(window), now, "w_morning");
        Assert.Equal(FireKind.Snooze, snoozePage.FiredAs);

        var carrier = new Event(new EventId("evt_trip"), Today, "Family trip", new TimeOnly(9, 0), new TimeOnly(10, 0), TagSet.Empty, null);
        var fallback = new FireRow(null, FireKind.Fallback, null, null, null, null, now, null, carrier.Id);
        var fallbackPage = await Page(
            new FakeStore(new FakeStoreViewBuilder().WithEvents([carrier]).WithFires(Today, new DayFires(Today, [fallback])).Build()),
            new FakeDayShapeReader(), now, "fallback");
        Assert.Equal(FireKind.Fallback, fallbackPage.FiredAs);
    }

    [Fact]
    public async Task The_footer_carries_the_disjoint_partition_plus_the_orphan_count()
    {
        var window = Window("w_morning", 9, 10);
        var shapes = Shapes(window);
        var now = Resolution.Resolve(Today, new TimeOnly(9, 0));
        var unprocessed = new TaskItem(new TaskId("t_unprocessed"), "Unprocessed", null, TagSet.Empty, null, null, null, null, now);
        var stale = Task("t_stale", duration: "10", createdAt: now - TimeSpan.FromDays(90));
        var orphan = Task("t_orphan", duration: "10", createdAt: now);
        var store = new FakeStore(new FakeStoreViewBuilder().WithTasks([unprocessed, stale, orphan]).Build());

        var page = await Page(store, shapes, now, "w_morning");

        Assert.Equal(new FooterCounts(1, 1, 1), page.Footer);
    }

    [Fact]
    public async Task A_failed_weather_fetch_is_reported_as_its_Dimension_id_rather_than_as_a_string()
    {
        var window = Window("w_morning", 9, 10);
        var shapes = Shapes(window);
        var now = Resolution.Resolve(Today, new TimeOnly(9, 0));
        var weatherTask = Task("t_weather", weather: "dry");
        var store = new FakeStore(new FakeStoreViewBuilder().WithTasks([weatherTask]).Build());
        var weatherSource = new FakeWeatherSource();
        weatherSource.SetCurrent(new Unavailable("boom"));

        var page = await Page(store, shapes, now, "w_morning", weatherSource);

        Assert.Equal([KnownDimensions.Weather], page.FailedFetches);
    }

    [Fact]
    public async Task Matching_on_splits_the_axes_the_Window_declares_from_the_axes_left_to_the_window_side_default()
    {
        var window = new AvailabilityWindow(new WindowId("w_morning"), "Morning", new TimeOnly(9, 0), new TimeOnly(10, 0),
            new TagSet(new Dictionary<DimensionId, IReadOnlyList<TagValue>>
            {
                [KnownDimensions.Location] = [new TagValue("home")],
                // A stray authored value on a Derived axis — AvailabilityWindow's own doc says
                // there is no window-side Duration Tag to author, but Matcher would never read
                // it even if one were there (WindowOrdinalValue only reads Derived off the
                // ceiling), so this must still land in Defaulted, not Declared.
                [KnownDimensions.Duration] = [new TagValue("30")],
            }, []));
        var shapes = Shapes(window);
        var now = Resolution.Resolve(Today, new TimeOnly(9, 0));
        var store = new FakeStore(new FakeStoreViewBuilder().Build());

        var page = await Page(store, shapes, now, "w_morning");

        Assert.Equal([new TagValue("home")], page.MatchingOn.Declared[KnownDimensions.Location]);
        Assert.False(page.MatchingOn.Defaulted.ContainsKey(KnownDimensions.Location));
        Assert.False(page.MatchingOn.Declared.ContainsKey(KnownDimensions.Duration));
        Assert.Equal([new TagValue("60")], page.MatchingOn.Defaulted[KnownDimensions.Duration]);
        Assert.Equal([new TagValue("low")], page.MatchingOn.Defaulted[KnownDimensions.MentalEnergy]);
        Assert.Empty(page.MatchingOn.Defaulted[KnownDimensions.WithWhom]);
        Assert.Empty(page.MatchingOn.Defaulted[KnownDimensions.Weather]);
    }

    [Fact]
    public async Task A_fire_row_with_no_span_and_no_Window_left_in_the_days_shape_is_not_a_page()
    {
        var now = Resolution.Resolve(Today, new TimeOnly(9, 0));
        var fired = new FireRow(new WindowId("w_gone"), FireKind.Window, null, null, null, null, now, null, null);
        var store = new FakeStore(new FakeStoreViewBuilder().WithFires(Today, new DayFires(Today, [fired])).Build());

        var outcome = await new ReadReminderPage(
            store, new FakeDayShapeReader(), KnownDimensions.Default, Resolution, Boundary, Thresholds,
            new DerivedTaskComposer([], new FakeDayShapeReader(), Boundary, new FixedTimeProvider(now)),
            new TickPlanner(new FakeDayShapeReader(), KnownDimensions.Default, Resolution, Boundary, Thresholds,
                new Uri("https://not-the-real-host.invalid/"), new DerivedTaskComposer([], new FakeDayShapeReader(), Boundary, new FixedTimeProvider(now))),
            new FakeWeatherSource(), new FixedTimeProvider(now))
            .ExecuteAsync(Today, "w_gone", CancellationToken.None);

        Assert.IsType<ReminderNotFound>(outcome.Value);
    }

    [Fact]
    public async Task The_read_writes_nothing()
    {
        var window = Window("w_morning", 9, 10);
        var shapes = Shapes(window);
        var now = Resolution.Resolve(Today, new TimeOnly(9, 0));
        var store = new FakeStore(new FakeStoreViewBuilder().WithTasks([Task("t1")]).Build());

        await Page(store, shapes, now, "w_morning");

        Assert.Empty(store.Mutations);
        Assert.Contains(Today, shapes.ReadDates);
    }

    private static Task<ReminderPage> Page(
        FakeStore store, FakeDayShapeReader shapes, DateTimeOffset now, string windowKey, FakeWeatherSource? weather = null) =>
        PageAsync(store, shapes, now, windowKey, weather);

    private static async Task<ReminderPage> PageAsync(
        FakeStore store, FakeDayShapeReader shapes, DateTimeOffset now, string windowKey, FakeWeatherSource? weather)
    {
        var timeProvider = new FixedTimeProvider(now);
        var composer = new DerivedTaskComposer([], shapes, Boundary, timeProvider);
        var planner = new TickPlanner(shapes, KnownDimensions.Default, Resolution, Boundary, Thresholds,
            new Uri("https://not-the-real-host.invalid/"), composer);
        var service = new ReadReminderPage(
            store, shapes, KnownDimensions.Default, Resolution, Boundary, Thresholds, composer, planner,
            weather ?? new FakeWeatherSource(), timeProvider);

        var outcome = await service.ExecuteAsync(Today, windowKey, CancellationToken.None);
        return Assert.IsType<ReminderPage>(outcome.Value);
    }

    private static FakeDayShapeReader Shapes(params AvailabilityWindow[] windows)
    {
        var reader = new FakeDayShapeReader();
        reader.Seed(Today, new DayShape(Today, windows, [], false));
        return reader;
    }

    private static AvailabilityWindow Window(string id, int start, int end) =>
        new(new WindowId(id), id, new TimeOnly(start, 0), new TimeOnly(end, 0), TagSet.Empty);

    private static AvailabilityWindow Window(string id, int startHour, int startMinute, int endHour, int endMinute) =>
        new(new WindowId(id), id, new TimeOnly(startHour, startMinute), new TimeOnly(endHour, endMinute), TagSet.Empty);

    private static TaskItem Task(
        string id,
        string? weather = null,
        string duration = "30",
        DateTimeOffset? createdAt = null) => new(
        new TaskId(id), id, null,
        new TagSet(
            new Dictionary<DimensionId, IReadOnlyList<TagValue>>
            {
                [KnownDimensions.Duration] = [new TagValue(duration)],
                [KnownDimensions.Weather] = weather is null ? [] : [new TagValue(weather)],
            },
            []),
        null, null, null, null, createdAt ?? Resolution.Resolve(Today, new TimeOnly(9, 0)).AddDays(-1));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
