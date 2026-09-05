using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Notifications;
using Xunit;

namespace TaskGuide.Domain.Tests;

public sealed class TimeToLivePolicyTests
{
    private static readonly DateTimeOffset WindowEnd = new(2050, 9, 5, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DayBoundary = new(2050, 9, 6, 0, 0, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public int GetUtcNowCalls { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            GetUtcNowCalls++;
            return now;
        }
    }

    [Fact]
    public void ttl_runs_to_the_window_end_for_a_window_fire_and_a_late_one()
    {
        Assert.Equal(WindowEnd, TimeToLivePolicy.For(FireKind.Window, WindowEnd, DayBoundary, new FixedTimeProvider(WindowEnd - TimeSpan.FromMinutes(1))));
    }

    [Fact]
    public void ttl_runs_to_the_window_end_for_a_snooze_re_fire_still_inside_the_span()
    {
        Assert.Equal(WindowEnd, TimeToLivePolicy.For(FireKind.Snooze, WindowEnd, DayBoundary, new FixedTimeProvider(WindowEnd - TimeSpan.FromMinutes(1))));
    }

    [Fact]
    public void ttl_runs_to_the_day_boundary_for_a_snooze_re_fire_past_the_span()
    {
        Assert.Equal(DayBoundary, TimeToLivePolicy.For(FireKind.Snooze, WindowEnd, DayBoundary, new FixedTimeProvider(WindowEnd)));
    }

    [Theory]
    [InlineData(FireKind.Unconditional)]
    [InlineData(FireKind.Fallback)]
    public void ttl_runs_to_the_day_boundary_for_an_unconditional_fire_and_a_fallback(FireKind kind)
    {
        Assert.Equal(DayBoundary, TimeToLivePolicy.For(kind, WindowEnd, DayBoundary, new FixedTimeProvider(WindowEnd)));
    }

    [Fact]
    public void ttl_reads_now_from_the_supplied_time_provider()
    {
        var clock = new RecordingTimeProvider(WindowEnd - TimeSpan.FromMinutes(1));

        Assert.Equal(WindowEnd, TimeToLivePolicy.For(FireKind.Snooze, WindowEnd, DayBoundary, clock));
        Assert.Equal(1, clock.GetUtcNowCalls);
    }
}
