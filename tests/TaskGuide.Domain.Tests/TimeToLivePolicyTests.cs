using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Notifications;
using Xunit;

namespace TaskGuide.Domain.Tests;

public sealed class TimeToLivePolicyTests
{
    private static readonly DateTimeOffset WindowEnd = new(2026, 9, 5, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DayBoundary = new(2026, 9, 6, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ttl_runs_to_the_window_end_for_a_window_fire_and_a_late_one()
    {
        Assert.Equal(WindowEnd, TimeToLivePolicy.For(FireKind.Window, WindowEnd, DayBoundary, WindowEnd - TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void ttl_runs_to_the_window_end_for_a_snooze_re_fire_still_inside_the_span()
    {
        Assert.Equal(WindowEnd, TimeToLivePolicy.For(FireKind.Snooze, WindowEnd, DayBoundary, WindowEnd - TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void ttl_runs_to_the_day_boundary_for_a_snooze_re_fire_past_the_span()
    {
        Assert.Equal(DayBoundary, TimeToLivePolicy.For(FireKind.Snooze, WindowEnd, DayBoundary, WindowEnd));
    }

    [Theory]
    [InlineData(FireKind.Unconditional)]
    [InlineData(FireKind.Fallback)]
    public void ttl_runs_to_the_day_boundary_for_an_unconditional_fire_and_a_fallback(FireKind kind)
    {
        Assert.Equal(DayBoundary, TimeToLivePolicy.For(kind, WindowEnd, DayBoundary, WindowEnd));
    }
}
