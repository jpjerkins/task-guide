using TaskGuide.Domain.Common;
using TaskGuide.Domain.Notifications;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.Domain.Time;
using Xunit;

namespace TaskGuide.Domain.Tests;

public sealed class GlancePolicyTests
{
    private static readonly TimeSpan Floor = TimeSpan.FromMinutes(30);

    private static GlanceState State(int count = 1) =>
        new(count, new NextWindow(
            new ResolvedWindow(
                new AvailabilityWindow(new WindowId("w_morning"), "Morning", new TimeOnly(9, 0), new TimeOnly(10, 0), TagSet.Empty),
                new DateTimeOffset(2026, 9, 5, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 9, 5, 10, 0, 0, TimeSpan.Zero)),
            Array.Empty<TaskItem>()));

    [Fact]
    public void a_first_glance_is_sent_without_a_previously_sent_payload()
    {
        Assert.True(GlancePolicy.ShouldSend(State(), null, TimeSpan.Zero, false, Floor));
    }

    [Fact]
    public void a_changed_glance_sends_at_the_floor_s_exact_boundary_but_not_one_tick_before_it()
    {
        Assert.False(GlancePolicy.ShouldSend(State(2), State(1), Floor - TimeSpan.FromTicks(1), false, Floor));
        Assert.True(GlancePolicy.ShouldSend(State(2), State(1), Floor, false, Floor));
    }

    [Fact]
    public void an_unchanged_glance_remains_suppressed_after_the_floor()
    {
        Assert.False(GlancePolicy.ShouldSend(State(), State(), Floor, false, Floor));
    }

    [Fact]
    public void a_window_start_preempts_the_floor()
    {
        Assert.True(GlancePolicy.ShouldSend(State(2), State(1), TimeSpan.Zero, true, Floor));
    }
}
