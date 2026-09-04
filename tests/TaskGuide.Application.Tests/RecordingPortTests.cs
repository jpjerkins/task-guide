using TaskGuide.TestSupport;
using Xunit;

namespace TaskGuide.Application.Tests;

/// <summary>
/// `tests/TEST-INVENTORY.md`, "Test support (#77)": the recording doubles for
/// <c>ITickHeartbeat</c>, <c>ITickLoop</c> and <c>IStartupSequence</c> keep their calls in order —
/// the fact each is built to prove.
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

    [Fact]
    public async Task A_recording_startup_sequence_keeps_its_phases_in_order()
    {
        var sequence = new RecordingStartupSequence();

        sequence.AssertRegistry();
        await sequence.SnapshotAsync(CancellationToken.None);
        await sequence.MigrateAsync(CancellationToken.None);
        await sequence.SweepRegistryAsync(CancellationToken.None);

        Assert.Equal(
            [nameof(sequence.AssertRegistry), nameof(sequence.SnapshotAsync), nameof(sequence.MigrateAsync), nameof(sequence.SweepRegistryAsync)],
            sequence.Phases);
    }
}
