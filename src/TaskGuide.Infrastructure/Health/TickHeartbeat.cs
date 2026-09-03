using TaskGuide.Application.Ports;

namespace TaskGuide.Infrastructure.Health;

/// <summary>
/// A dependency-free state holder for the last tick, split off <see cref="HealthReporter"/> (#69):
/// the tick loop records, the reporter only reads.
/// </summary>
public sealed class TickHeartbeat : ITickHeartbeat
{
    // DateTimeOffset? can't be volatile (not atomically readable); store the UTC ticks instead,
    // 0 meaning "never ticked", read/written via Interlocked so no lock is needed on either side.
    private long _lastTickUtcTicks;

    public void RecordTick(DateTimeOffset at) => Interlocked.Exchange(ref _lastTickUtcTicks, at.UtcTicks);

    public DateTimeOffset? LastTick
    {
        get
        {
            var lastTickUtcTicks = Interlocked.Read(ref _lastTickUtcTicks);
            return lastTickUtcTicks == 0 ? null : new DateTimeOffset(lastTickUtcTicks, TimeSpan.Zero);
        }
    }
}
