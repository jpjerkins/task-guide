using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Notifications;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Time;
using Xunit;

namespace TaskGuide.Domain.Tests;

public sealed class TimeToLivePolicyTests
{
    private static readonly DateTimeOffset WindowEnd = new(2050, 9, 5, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DayBoundary = new(2050, 9, 6, 0, 0, 0, TimeSpan.Zero);

    private static ResolvedWindow Window() => new(
        new AvailabilityWindow(new WindowId("w_ttl"), "TTL", new TimeOnly(14, 0), new TimeOnly(15, 0), TagSet.Empty),
        WindowEnd.AddHours(-1),
        WindowEnd);

    [Fact]
    public void ttl_runs_to_the_window_end_for_a_window_fire_and_a_late_one()
    {
        Assert.Equal(WindowEnd, TimeToLivePolicy.For(new WindowFire(Window()), DayBoundary));
    }

    [Fact]
    public void ttl_runs_to_the_window_end_for_a_snooze_re_fire_still_inside_the_span()
    {
        Assert.Equal(WindowEnd, TimeToLivePolicy.For(new InWindowSnooze(Window()), DayBoundary));
    }

    [Fact]
    public void ttl_runs_to_the_day_boundary_for_a_snooze_re_fire_past_the_span()
    {
        Assert.Equal(DayBoundary, TimeToLivePolicy.For(new PastWindowSnooze(), DayBoundary));
    }

    [Fact]
    public void ttl_runs_to_the_day_boundary_for_an_unconditional_fire_and_a_fallback()
    {
        Assert.Equal(DayBoundary, TimeToLivePolicy.For(new UnconditionalFire(), DayBoundary));
        Assert.Equal(DayBoundary, TimeToLivePolicy.For(new FallbackFire(), DayBoundary));
    }
}
