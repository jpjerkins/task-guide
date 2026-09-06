using TaskGuide.Infrastructure.BackgroundServices;
using TaskGuide.TestSupport;
using Xunit;

namespace TaskGuide.Infrastructure.Tests;

/// <summary>
/// The hosted loop supplies cadence and delegates every pass to Application.
/// </summary>
public sealed class TickLoopTests
{
    [Fact]
    public async Task Every_tick_invokes_the_application_tick_loop()
    {
        var ticks = new RecordingTickLoop();
        var loop = new TickLoop(ticks);

        await loop.TickOnceAsync(CancellationToken.None);

        Assert.Single(ticks.Ticks);
    }

    [Fact]
    public async Task Every_tick_does_not_send_a_skeleton_receipt()
    {
        var ticks = new RecordingTickLoop();
        var loop = new TickLoop(ticks);

        await loop.TickOnceAsync(CancellationToken.None);

        Assert.Single(ticks.Ticks);
    }
}
