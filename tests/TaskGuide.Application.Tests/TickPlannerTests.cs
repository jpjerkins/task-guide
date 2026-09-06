using TaskGuide.Application.Firing;
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

public sealed class TickPlannerTests
{
    private static readonly DayBoundary Boundary = new(TimeZoneInfo.Utc);
    private static readonly ClockTimeResolution Resolution = new(Boundary);
    private static readonly DateOnly Today = new(2026, 9, 5);
    private static readonly DateTimeOffset Now = Resolution.Resolve(Today, new TimeOnly(9, 0));
    private static readonly StaleThresholds Thresholds = new(TimeSpan.FromDays(30), 3);

    [Fact]
    public void A_Window_fires_at_its_start()
    {
        var window = Window("w_morning", 9, 10);
        var task = Task("t_morning");
        var shapes = Shapes(window);
        var plan = Planner(shapes).Plan(View(task), Now, EmptyFetched, []);

        var intent = Assert.Single(plan.Fires);
        var resolved = Assert.IsType<WindowFire>(intent.Kind.Value).Window;
        Assert.Equal(window.Id, resolved.Window.Id);
        Assert.Equal(window.Id, intent.FireRow.WindowId);
        Assert.Equal(FireKind.Window, intent.FireRow.Kind);
        Assert.Equal(1, intent.FireRow.Matched);
        Assert.Equal([task], intent.Shortlist);
        var reminder = Assert.IsType<Reminder>(typeof(FireIntent).GetProperty("Reminder")?.GetValue(intent));
        Assert.Equal(resolved.End, reminder.TimeToLive);
        Assert.Null(plan.Glance);
    }

    [Fact]
    public void A_Window_that_matches_nothing_sends_nothing()
    {
        var shapes = Shapes(Window("w_morning", 9, 10));
        var plan = Planner(shapes).Plan(View(Task("t_garage", location: "garage")), Now, EmptyFetched, []);

        Assert.Empty(plan.Fires);
    }

    [Fact]
    public void A_Window_is_not_re_fired_part_way_through_however_long_it_is()
    {
        var window = Window("w_morning", 9, 12);
        var shapes = Shapes(window);
        var fired = new FireRow(window.Id, FireKind.Window, window.Name, window.Start, window.End,
            null, Now.AddHours(-1), 1, null);

        var plan = Planner(shapes).Plan(View(Task("t_morning"), new DayFires(Today, [fired])), Now, EmptyFetched, []);

        Assert.Empty(plan.Fires);
    }

    [Fact]
    public void There_is_no_daily_cap_N_authored_Windows_all_matching_fire_N_times()
    {
        var plan = Planner(Shapes(Window("w_morning", 9, 10), Window("w_late_morning", 9, 11)))
            .Plan(View(Task("t_morning")), Now, EmptyFetched, []);

        Assert.Equal(2, plan.Fires.Count);
    }

    [Fact]
    public void A_Window_whose_start_passed_while_the_service_was_down_fires_late_inside_its_own_span_with_the_ceiling_re_derived_from_now_to_end()
    {
        var window = Window("w_morning", 8, 10);
        var plan = Planner(Shapes(window)).Plan(
            View(Task("t_hour", duration: "60"), Task("t_two_hours", duration: "Longer")),
            Now,
            EmptyFetched,
            []);

        var intent = Assert.Single(plan.Fires);
        Assert.Equal(Resolution.Resolve(Today, new TimeOnly(10, 0)), intent.Reminder.TimeToLive);
        Assert.Equal([Task("t_hour", duration: "60")], intent.Shortlist);
    }

    [Fact]
    public void A_Window_whose_span_closed_while_the_service_was_down_is_silent()
    {
        var plan = Planner(Shapes(Window("w_morning", 8, 9)))
            .Plan(View(Task("t_morning")), Now, EmptyFetched, []);

        Assert.Empty(plan.Fires);
    }

    [Fact]
    public void A_long_outage_produces_no_burst_on_return()
    {
        var plan = Planner(Shapes(
                Window("w_dawn", 6, 7),
                Window("w_early", 7, 8),
                Window("w_morning", 8, 10)))
            .Plan(View(Task("t_morning")), Now, EmptyFetched, []);

        var intent = Assert.Single(plan.Fires);
        Assert.Equal(new WindowId("w_morning"), intent.FireRow.WindowId);
    }

    [Fact]
    public void The_complete_matched_set_is_ranked_rather_than_filtered()
    {
        var older = Task("t_older");
        var newer = Task("t_newer", createdAt: Now);
        var plan = Planner(Shapes(Window("w_morning", 9, 10)))
            .Plan(View(older, newer), Now, EmptyFetched, []);

        Assert.Equal([older, newer], Assert.Single(plan.Fires).Shortlist);
    }

    [Fact]
    public void An_unavailable_forecast_keeps_a_currently_matching_Task_and_sorts_it_after_known_counts()
    {
        var window = Window("w_morning", 9, 10);
        var weatherTask = Task("t_weather", weather: "dry");
        var knownTask = Task("t_known");
        var fetched = new Dictionary<DimensionId, IReadOnlyList<TagValue>>
        {
            [KnownDimensions.Weather] = [new TagValue("dry")],
        };
        var plan = Planner(Shapes(window)).Plan(
            View(weatherTask, knownTask), Now, fetched, [KnownDimensions.Weather]);

        var intent = Assert.Single(plan.Fires);
        Assert.Equal([knownTask, weatherTask], intent.Shortlist);
    }

    private static TickPlanner Planner(FakeDayShapeReader shapes) =>
        new(shapes, KnownDimensions.Default, Resolution, Boundary, Thresholds, new Uri("https://not-the-real-host.invalid/"));

    private static FakeStoreView View(params TaskItem[] tasks) =>
        new FakeStoreViewBuilder().WithTasks(tasks).WithFires(Today, new DayFires(Today, [])).Build();

    private static FakeStoreView View(TaskItem task, DayFires fires) =>
        new FakeStoreViewBuilder().WithTasks([task]).WithFires(Today, fires).Build();

    private static FakeStoreView View(TaskItem first, TaskItem second) =>
        new FakeStoreViewBuilder().WithTasks([first, second]).Build();

    private static FakeDayShapeReader Shapes(params AvailabilityWindow[] windows)
    {
        var reader = new FakeDayShapeReader();
        reader.Seed(Today, new DayShape(Today, windows, [], false));
        return reader;
    }

    private static AvailabilityWindow Window(string id, int start, int end) =>
        new(new WindowId(id), id, new TimeOnly(start, 0), new TimeOnly(end, 0), TagSet.Empty);

    private static TaskItem Task(
        string id,
        string? weather = null,
        string? location = null,
        string duration = "30",
        DateTimeOffset? createdAt = null) => new(
        new TaskId(id), id, null,
        new TagSet(
            new Dictionary<DimensionId, IReadOnlyList<TagValue>>
            {
                [KnownDimensions.Duration] = [new TagValue(duration)],
                [KnownDimensions.Weather] = weather is null ? [] : [new TagValue(weather)],
                [KnownDimensions.Location] = location is null ? [] : [new TagValue(location)],
            },
            []),
        null, null, null, null, createdAt ?? Now.AddDays(-1));

    private static readonly IReadOnlyDictionary<DimensionId, IReadOnlyList<TagValue>> EmptyFetched =
        new Dictionary<DimensionId, IReadOnlyList<TagValue>>();
}
