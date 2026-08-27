namespace TaskGuide.Application.Ports;

/// <summary>
/// <b>The service was capable of firing a Reminder recently.</b> A component belongs to Liveness
/// if its failure would make <c>no Reminder ⟺ nothing fit</c> a lie — that test is what keeps the
/// predicate from accreting checks forever.
/// </summary>
/// <remarks>
/// In: the tick loop advanced recently, the store is readable, the store is writable. Out: load,
/// memory, latency (host monitoring's, not ours) and Pushover reachability (already answered by
/// retry, and unbuildable anyway — an outage at Pushover cannot be reported through Pushover).
/// <para>
/// <b>Read health is a parse, not a stat</b> — a stat succeeds on the truncated file a failed
/// write leaves behind. <b>Write health is taken from work already being done</b>: the retention
/// sweep touches the filesystem every tick, so its outcome <em>is</em> a write check on a
/// 30-second cadence. Observed rather than probed.
/// </para>
/// <para>
/// This is the container's health check, so a <b>wedged</b> loop is restarted automatically with
/// no alert and no surface; a <b>dead</b> service stops the heartbeat and something external
/// alerts, because a dead service cannot report its own death.
/// </para>
/// </remarks>
public interface IHealthReporter
{
    HealthReport Current();
}

/// <param name="Ok">The boolean, for the two automatic consumers.</param>
/// <param name="LastTick">Also the one line Liveness surfaces in the app — the last fire, on a screen that already exists.</param>
public sealed record HealthReport(bool Ok, DateTimeOffset? LastTick, StorageHealth Storage, TimeSpan Uptime);

public sealed record StorageHealth(bool Readable, bool Writable);
