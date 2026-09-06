using TaskGuide.Application.Firing;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Notifications;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.Domain.Time;
using TaskGuide.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace TaskGuide.Application.Tests;

public sealed class GlanceSendingTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task a_first_glance_is_sent_without_a_previously_sent_payload()
    {
        var sender = new RecordingGlanceSender();
        var scheduling = new GlanceScheduling(sender);

        await scheduling.SendAsync(State("first"), Now, CancellationToken.None);

        Assert.Equal([State("first")], sender.Sent);
    }

    [Fact]
    public async Task a_changed_glance_sends_at_the_floors_exact_boundary_but_not_one_tick_before()
    {
        var sender = new RecordingGlanceSender();
        var scheduling = new GlanceScheduling(sender);

        await scheduling.SendAsync(State("first"), Now, CancellationToken.None);
        await scheduling.SendAsync(State("changed"), Now.AddMinutes(30).AddSeconds(-30), CancellationToken.None);
        Assert.Single(sender.Sent);
        await scheduling.SendAsync(State("changed"), Now.AddMinutes(30), CancellationToken.None);

        Assert.Equal([State("first"), State("changed")], sender.Sent);
    }

    [Fact]
    public async Task an_unchanged_glance_remains_suppressed_after_the_floor()
    {
        var sender = new RecordingGlanceSender();
        var scheduling = new GlanceScheduling(sender);

        await scheduling.SendAsync(State("same"), Now, CancellationToken.None);
        await scheduling.SendAsync(State("same"), Now.AddHours(1), CancellationToken.None);

        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task a_window_start_preempts_the_floor()
    {
        var sender = new RecordingGlanceSender();
        var scheduling = new GlanceScheduling(sender);
        var window = Window("morning");

        await scheduling.SendAsync(new GlanceState(1, new NextWindow(window, [])), Now, CancellationToken.None);
        await scheduling.SendAsync(new GlanceState(1, new InsideWindow(window, [], 0)), Now.AddSeconds(30), CancellationToken.None);

        Assert.Equal(2, sender.Sent.Count);
    }

    [Fact]
    public async Task a_window_end_does_not_preempt_the_floor()
    {
        var sender = new RecordingGlanceSender();
        var scheduling = new GlanceScheduling(sender);
        var window = Window("morning");

        await scheduling.SendAsync(new GlanceState(1, new InsideWindow(window, [], 0)), Now, CancellationToken.None);
        await scheduling.SendAsync(new GlanceState(1, new NextWindow(Window("afternoon"), [])), Now.AddSeconds(30), CancellationToken.None);

        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task one_retry_at_the_next_tick_ignoring_the_floor_never_two()
    {
        var sender = new RecordingGlanceSender();
        sender.FailNextSend();
        var scheduling = new GlanceScheduling(sender);

        await scheduling.SendAsync(State("first"), Now, CancellationToken.None);
        await scheduling.SendAsync(State("first"), Now.AddSeconds(30), CancellationToken.None);
        await scheduling.SendAsync(State("changed"), Now.AddMinutes(1), CancellationToken.None);

        Assert.Equal([State("first"), State("first")], sender.Sent);
    }

    [Fact]
    public async Task no_weather_tagged_active_task_no_api_call()
    {
        var boundary = new DayBoundary(TimeZoneInfo.Utc);
        var planner = new TickPlanner(
            new FakeDayShapeReader(),
            KnownDimensions.Default,
            new ClockTimeResolution(boundary),
            boundary,
            new StaleThresholds(TimeSpan.FromDays(30), 3),
            new Uri("https://not-the-real-host.invalid/"));
        var store = new FakeStore(new FakeStoreViewBuilder().WithTasks([ActiveTaskWithoutWeather()]).Build());
        var weather = new FakeWeatherSource();
        var executor = new TickExecutor(
            store,
            new RecordingReminderSender(),
            new RecordingGlanceSender(),
            new RecordingTickHeartbeat(),
            new NoopRetention(),
            boundary,
            NullLogger<TickExecutor>.Instance);

        await new TickService(store, planner, executor, weather).TickAsync(Now, CancellationToken.None);

        Assert.Equal(0, weather.CurrentCallCount);
    }

    private static GlanceState State(string windowId) => new(1, new NextWindow(Window(windowId), []));

    private static ResolvedWindow Window(string id) => new(
        new AvailabilityWindow(new WindowId(id), id, new TimeOnly(9, 0), new TimeOnly(10, 0), TagSet.Empty),
        Now,
        Now.AddHours(1));

    private static TaskItem ActiveTaskWithoutWeather() => new(
        new TaskId("task"),
        "Task",
        null,
        new TagSet(new Dictionary<DimensionId, IReadOnlyList<TagValue>>
        {
            [KnownDimensions.Duration] = [new TagValue("30")],
            [KnownDimensions.Weather] = [],
        }, []),
        null, null, null, null, Now.AddDays(-1));

    private sealed class NoopRetention : IFireRetention
    {
        public FireSweepResult Sweep(DateOnly today) => new([], []);
    }
}
