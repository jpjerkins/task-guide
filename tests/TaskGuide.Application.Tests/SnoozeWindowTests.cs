using TaskGuide.Application.Reminders;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Time;
using TaskGuide.TestSupport;
using Xunit;

namespace TaskGuide.Application.Tests;

public sealed class SnoozeWindowTests
{
    [Fact]
    public async Task A_Snooze_writes_its_pending_Fire_record_row_immediately()
    {
        var date = new DateOnly(2026, 9, 5);
        var windowId = new WindowId("w_morning");
        var fired = new FireRow(windowId, FireKind.Window, "Morning", new TimeOnly(9, 0), new TimeOnly(10, 0), null, new DateTimeOffset(2026, 9, 5, 9, 0, 0, TimeSpan.Zero), 1, null);
        var store = new FakeStore(new FakeStoreViewBuilder().WithFires(date, new DayFires(date, [fired])).Build());

        await new SnoozeWindow(store, new FixedTimeProvider(new DateTimeOffset(2026, 9, 5, 9, 0, 0, TimeSpan.Zero)), new DayBoundary(TimeZoneInfo.Utc)).ExecuteAsync(date, windowId, CancellationToken.None);

        var snooze = Assert.Single(store.Read().FiresOn(date).Rows, row => row.Kind == FireKind.Snooze);
        Assert.Equal(windowId, snooze.WindowId);
        Assert.Equal(new DateTimeOffset(2026, 9, 5, 9, 15, 0, TimeSpan.Zero), snooze.DueAt);
        Assert.Null(snooze.FiredAt);
    }

    [Fact]
    public async Task Snooze_refuses_a_re_fire_crossing_the_Reminders_day_boundary_with_the_unavailable_control_line()
    {
        var date = new DateOnly(2026, 9, 5);
        var windowId = new WindowId("w_late");
        var fired = new FireRow(windowId, FireKind.Window, "Late", new TimeOnly(23, 45), new TimeOnly(23, 55), null, new DateTimeOffset(2026, 9, 5, 23, 45, 0, TimeSpan.Zero), 1, null);
        var store = new FakeStore(new FakeStoreViewBuilder().WithFires(date, new DayFires(date, [fired])).Build());

        var outcome = await new SnoozeWindow(store, new FixedTimeProvider(new DateTimeOffset(2026, 9, 5, 23, 56, 0, TimeSpan.Zero)), new DayBoundary(TimeZoneInfo.Utc)).ExecuteAsync(date, windowId, CancellationToken.None);

        Assert.Equal(new SnoozeUnavailable("Snooze ends at midnight"), outcome.Value);
        Assert.Empty(store.Mutations);
    }

    [Fact]
    public async Task A_stale_Reminders_Snooze_refusal_says_This_reminder_was_for_yesterday()
    {
        var date = new DateOnly(2026, 9, 5);
        var windowId = new WindowId("w_evening");
        var fired = new FireRow(windowId, FireKind.Window, "Evening", new TimeOnly(22, 0), new TimeOnly(23, 0), null, new DateTimeOffset(2026, 9, 5, 22, 0, 0, TimeSpan.Zero), 1, null);
        var store = new FakeStore(new FakeStoreViewBuilder().WithFires(date, new DayFires(date, [fired])).Build());

        var outcome = await new SnoozeWindow(store, new FixedTimeProvider(new DateTimeOffset(2026, 9, 6, 0, 5, 0, TimeSpan.Zero)), new DayBoundary(TimeZoneInfo.Utc)).ExecuteAsync(date, windowId, CancellationToken.None);

        Assert.Equal(new SnoozeUnavailable("This reminder was for yesterday"), outcome.Value);
        Assert.Empty(store.Mutations);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
