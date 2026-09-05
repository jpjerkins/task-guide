using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Notifications;
using Xunit;

namespace TaskGuide.Domain.Tests;

public sealed class TimeToLivePolicyTests
{
    private static readonly DateTimeOffset WindowEnd = new(2026, 9, 5, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DayBoundary = new(2026, 9, 6, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(FireKind.Window)]
    [InlineData(FireKind.Snooze)]
    public void Ttl_runs_to_the_window_end_for_a_window_fire_and_a_late_one(FireKind kind)
    {
        Assert.Equal(WindowEnd, TimeToLivePolicy.For(kind, WindowEnd, DayBoundary));
    }

    [Theory]
    [InlineData(FireKind.Unconditional)]
    [InlineData(FireKind.Fallback)]
    public void Ttl_runs_to_the_day_boundary_for_an_unconditional_fire_and_a_fallback(FireKind kind)
    {
        Assert.Equal(DayBoundary, TimeToLivePolicy.For(kind, WindowEnd, DayBoundary));
    }
}
