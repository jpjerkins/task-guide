using System.Text.Json;
using TaskGuide.Application.Ports;
using TaskGuide.Infrastructure.Storage;

namespace TaskGuide.Infrastructure.Health;

/// <summary>
/// <see cref="IHealthReporter"/> for the walking skeleton (#51): liveness is tick freshness plus
/// a store read, not an HTTP 200. Peer to <see cref="JsonStore"/> — both understand the physical
/// file layout — rather than reaching through <see cref="IStore"/>, because "read health parses
/// the file" (the XML docs on <see cref="IHealthReporter"/>) means re-parsing what's on disk
/// right now, not trusting the in-memory cache a corrupting write happened after.
/// </summary>
/// <remarks>
/// <b>Write health is a stand-in, not the documented mechanism.</b> The real design reads write
/// health off the retention sweep's outcome — work already touching the filesystem every tick —
/// specifically so it is never a probe. That sweep doesn't exist yet (#51 is storage substrate
/// plus one endpoint, not the domain). Rather than fabricate a probe that contradicts "not a
/// probe", <see cref="StorageHealth.Writable"/> is hardcoded <c>true</c> here, and this needs
/// revisiting the moment the retention sweep lands.
/// </remarks>
public sealed class HealthReporter(string dataDir) : IHealthReporter
{
    /// <summary>
    /// A tick loop with a ~30s cadence: three missed ticks (90s) before the reporter calls it
    /// stale. Not documented anywhere as an exact number — a deliberate, simple multiple of the
    /// interval, chosen so one slow tick doesn't flap <c>ok</c>.
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
        const bool writable = true; // see remarks — stand-in until the retention sweep exists

        var lastTickUtcTicks = Interlocked.Read(ref _lastTickUtcTicks);
        DateTimeOffset? lastTick = lastTickUtcTicks == 0 ? null : new DateTimeOffset(lastTickUtcTicks, TimeSpan.Zero);
        var fresh = lastTick is { } tick && now - tick <= StalenessThreshold;
        var ok = fresh && readable && writable;

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
