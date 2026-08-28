using System.Text.Json;
using TaskGuide.Application.Ports;
using TaskGuide.Infrastructure.Storage;

namespace TaskGuide.Infrastructure.Health;

/// <summary>
/// <see cref="IHealthReporter"/> for the walking skeleton (#51): liveness is tick freshness plus
/// a store read, not an HTTP 200. Read health re-parses <c>tasks.json</c> directly (peer to
/// <see cref="JsonStore"/> — both understand the physical file layout — because "read health
/// parses the file", the XML docs on <see cref="IHealthReporter"/>, means re-parsing what's on
/// disk right now, not trusting the in-memory cache a corrupting write happened after). Write
/// health is read off <paramref name="store"/>'s <see cref="IStore.LastWriteSucceeded"/> —
/// <b>observed, not probed</b>: work the store was already doing, never a synthetic write
/// manufactured for the health check.
/// </summary>
public sealed class HealthReporter(IStore store, string dataDir) : IHealthReporter
{
    /// <summary>
    /// A tick loop with a ~30s cadence: three missed ticks (90s) before the reporter calls it
    /// stale — tolerant of one lost tick and one slow one, and still naming a stall inside two
    /// minutes. Approved 2026-08-27 and recorded in <c>docs/adr/0005-firing-engine.md</c>.
    /// <para>
    /// This is <b>3× the tick interval, not an independent number</b>. If the cadence changes,
    /// change this with it — decoupling them lets a slower tick read as permanently unhealthy.
    /// </para>
    /// </summary>
    public static readonly TimeSpan StalenessThreshold = TimeSpan.FromSeconds(90);

    private readonly string _tasksPath = Path.Combine(dataDir, "tasks.json");
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    // DateTimeOffset? can't be volatile (not atomically readable); store the UTC ticks instead,
    // 0 meaning "never ticked", read/written via Interlocked so no lock is needed on either side.
    private long _lastTickUtcTicks;

    public void RecordTick(DateTimeOffset at) => Interlocked.Exchange(ref _lastTickUtcTicks, at.UtcTicks);

    public HealthReport Current()
    {
        var now = DateTimeOffset.UtcNow;
        var readable = TryParseTasksJson();
        var writable = store.LastWriteSucceeded; // null = no write attempted yet since boot

        var lastTickUtcTicks = Interlocked.Read(ref _lastTickUtcTicks);
        DateTimeOffset? lastTick = lastTickUtcTicks == 0 ? null : new DateTimeOffset(lastTickUtcTicks, TimeSpan.Zero);
        var fresh = lastTick is { } tick && now - tick <= StalenessThreshold;

        // Unknown does not fail `ok`: a service that simply hasn't written anything yet (nothing
        // captured since boot) is not evidence of anything wrong. Only an *observed* failed write
        // (writable == false) does — "observed, not probed" applies to the predicate too, not
        // just to how the value is obtained.
        var ok = fresh && readable && writable != false;

        return new HealthReport(ok, lastTick, new StorageHealth(readable, writable), now - _startedAt);
    }

    private bool TryParseTasksJson()
    {
        if (!File.Exists(_tasksPath)) return true;

        try
        {
            TaskCodec.Read(File.ReadAllText(_tasksPath));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
