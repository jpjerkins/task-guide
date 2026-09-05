using TaskGuide.TestSupport;
using Xunit;

namespace TaskGuide.Application.Tests;

/// <summary>
/// `tests/TEST-INVENTORY.md`, "Test support (#77)": the recording doubles for
/// <c>ITickHeartbeat</c> and <c>ITickLoop</c> keep their calls in order — the fact each is built
/// to prove.
/// </summary>
public sealed class RecordingPortTests
{
    [Fact]
    public void A_recording_heartbeat_keeps_every_tick_instant_in_order()
    {
        var heartbeat = new RecordingTickHeartbeat();
        var first = new DateTimeOffset(2026, 9, 5, 9, 0, 0, TimeSpan.Zero);
        var second = first.AddSeconds(30);

        heartbeat.RecordTick(first);
        heartbeat.RecordTick(second);

        Assert.Equal([first, second], heartbeat.Ticks);
    }
}
