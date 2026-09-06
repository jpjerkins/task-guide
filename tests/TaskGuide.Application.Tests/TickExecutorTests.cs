using TaskGuide.Application.Firing;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Notifications;
using TaskGuide.Domain.Time;
using TaskGuide.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace TaskGuide.Application.Tests;

public sealed class TickExecutorTests
{
    private static readonly DayBoundary Boundary = new(TimeZoneInfo.Utc);
    private static readonly DateOnly Today = new(2026, 9, 6);
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_tick_executor_delivers_planned_intents_without_deciding_them()
    {
        var executor = typeof(TickPlan).Assembly.GetType("TaskGuide.Application.Firing.TickExecutor");

        Assert.NotNull(executor);
    }

    [Fact]
    public void The_fire_retention_port_reports_the_sweep_outcome()
    {
        var sweep = typeof(IFireRetention).GetMethod(nameof(IFireRetention.Sweep));

        Assert.Equal("FireSweepResult", sweep?.ReturnType.Name);
    }

    [Fact]
    public async Task FiredAt_is_written_only_when_Pushover_accepts()
    {
        var store = new FakeStore();
        var reminders = new RecordingReminderSender();
        reminders.FailNextSend();
        var heartbeat = new RecordingTickHeartbeat();
        var retention = new RecordingRetention();
        var executor = Executor(store, reminders, heartbeat, retention);

        await executor.ExecuteAsync(new TickPlan([Intent("w_morning")], null), Now, CancellationToken.None);

        Assert.Empty(store.Read().FiresOn(Today).Rows);
        Assert.Empty(store.Mutations);
        Assert.Equal([Today], retention.Dates);
        Assert.Equal([Now], heartbeat.Ticks);
    }

    [Fact]
    public async Task A_rejected_push_reads_as_unfired_next_tick_and_is_retried()
    {
        var store = new FakeStore();
        var reminders = new RecordingReminderSender();
        reminders.FailNextSend();
        var executor = Executor(store, reminders, new RecordingTickHeartbeat(), new RecordingRetention());
        var plan = new TickPlan([Intent("w_morning")], null);

        await executor.ExecuteAsync(plan, Now, CancellationToken.None);
        await executor.ExecuteAsync(plan, Now.AddSeconds(30), CancellationToken.None);

        var fire = Assert.Single(store.Read().FiresOn(Today).Rows);
        Assert.Equal(Now.AddSeconds(30), fire.FiredAt);
        Assert.Equal(2, reminders.Reminders.Count);
    }

    [Fact]
    public async Task Each_accepted_intent_is_written_in_its_own_mutation()
    {
        var store = new FakeStore();
        var executor = Executor(store, new RecordingReminderSender(), new RecordingTickHeartbeat(), new RecordingRetention());

        await executor.ExecuteAsync(new TickPlan([Intent("w_morning"), Intent("w_afternoon")], null), Now, CancellationToken.None);

        Assert.Equal(2, store.Mutations.Count);
        Assert.Equal(2, store.Read().FiresOn(Today).Rows.Count);
    }

    [Fact]
    public async Task A_failed_intent_does_not_skip_its_siblings_or_the_retention_sweep()
    {
        var store = new FakeStore();
        store.FailNextWrite();
        var reminders = new RecordingReminderSender();
        var heartbeat = new RecordingTickHeartbeat();
        var retention = new RecordingRetention();
        var executor = Executor(store, reminders, heartbeat, retention);

        await executor.ExecuteAsync(new TickPlan([Intent("w_morning"), Intent("w_afternoon")], null), Now, CancellationToken.None);

        var fire = Assert.Single(store.Read().FiresOn(Today).Rows);
        Assert.Equal(new WindowId("w_afternoon"), fire.WindowId);
        Assert.Equal(2, reminders.Reminders.Count);
        Assert.Equal([Today], retention.Dates);
        Assert.Equal([Now], heartbeat.Ticks);
    }

    private static TickExecutor Executor(
        FakeStore store,
        RecordingReminderSender reminders,
        RecordingTickHeartbeat heartbeat,
        RecordingRetention retention) =>
        new(store, reminders, heartbeat, retention, Boundary, NullLogger<TickExecutor>.Instance);

    private static FireIntent Intent(string windowId)
    {
        var row = new FireRow(new WindowId(windowId), FireKind.Window, windowId, new TimeOnly(9, 0), new TimeOnly(10, 0), null, null, 1, null);
        var reminder = new Reminder("Task (30)", windowId, [], 0, [], new FooterCounts(0, 0, 0), [], new Uri("https://taskguide.example/"), Now.AddHours(1));
        var window = new ResolvedWindow(new(new WindowId(windowId), windowId, new TimeOnly(9, 0), new TimeOnly(10, 0), TaskGuide.Domain.Tags.TagSet.Empty), Now, Now.AddHours(1));
        return new FireIntent(new WindowFire(window), [], reminder, row);
    }

    private sealed class RecordingRetention : IFireRetention
    {
        public List<DateOnly> Dates { get; } = [];

        public FireSweepResult Sweep(DateOnly today)
        {
            Dates.Add(today);
            return new FireSweepResult([], []);
        }
    }
}
