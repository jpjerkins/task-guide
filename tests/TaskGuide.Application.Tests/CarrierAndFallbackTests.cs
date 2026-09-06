using TaskGuide.Application.Firing;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Time;
using Xunit;

namespace TaskGuide.Application.Tests;

public sealed class CarrierAndFallbackTests
{
    private static readonly DayBoundary Boundary = new(TimeZoneInfo.Utc);
    private static readonly ClockTimeResolution Resolution = new(Boundary);
    private static readonly DateOnly Today = new(2026, 9, 5);

    [Fact]
    public void A_windowless_runway_day_fires_the_fallback_push_at_exactly_11_00a()
    {
        var now = Resolution.Resolve(Today, FiringPolicy.FallbackPushEarliest);
        var carrier = new Event(
            new EventId("evt_carrier"), Today.AddDays(3), "Trip", new TimeOnly(9, 0), new TimeOnly(10, 0), TagSet.Empty, null);

        var due = Fallback.IsDue(carrier, [], new DayFires(Today, []), now, Resolution, Boundary);

        Assert.True(due);
    }

    [Fact]
    public void A_runway_day_whose_Windows_were_all_eaten_fires_it_when_the_last_span_closes()
    {
        var now = Resolution.Resolve(Today, new TimeOnly(12, 0));
        var carrier = Carrier();
        var window = new AvailabilityWindow(new WindowId("w_morning"), "Morning", new TimeOnly(9, 0), new TimeOnly(10, 0), TagSet.Empty);

        var due = Fallback.IsDue(carrier, [window], new DayFires(Today, []), now, Resolution, Boundary);

        Assert.True(due);
    }

    [Fact]
    public void The_fallback_push_never_fires_after_the_day_boundary()
    {
        var tomorrow = Resolution.Resolve(Today.AddDays(1), new TimeOnly(0, 0));

        var due = Fallback.IsDue(Carrier(), [], new DayFires(Today, []), tomorrow, Resolution, Boundary);

        Assert.False(due);
    }

    [Fact]
    public void A_non_runway_windowless_day_fires_nothing()
    {
        var now = Resolution.Resolve(Today, new TimeOnly(12, 0));

        var due = Fallback.IsDue(null, [], new DayFires(Today, []), now, Resolution, Boundary);

        Assert.False(due);
    }

    [Fact]
    public void A_fired_row_carries_the_duty_without_reading_the_audit_field()
    {
        var now = Resolution.Resolve(Today, new TimeOnly(12, 0));
        var fired = new FireRow(
            new WindowId("w_morning"), FireKind.Window, "Morning", new TimeOnly(9, 0), new TimeOnly(10, 0),
            null, now.AddHours(-1), 1, Carried: null);

        var due = Fallback.IsDue(Carrier(), [], new DayFires(Today, [fired]), now, Resolution, Boundary);

        Assert.False(due);
    }

    private static Event Carrier() => new(
        new EventId("evt_carrier"), Today.AddDays(3), "Trip", new TimeOnly(9, 0), new TimeOnly(10, 0), TagSet.Empty, null);
}
