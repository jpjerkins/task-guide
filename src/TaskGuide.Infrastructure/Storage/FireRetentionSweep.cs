using TaskGuide.Application.Ports;

namespace TaskGuide.Infrastructure.Storage;

/// <summary>Infrastructure adapter for the tick's unguarded fire-record retention sweep.</summary>
public sealed class FireRetentionSweep(string dataDir) : IFireRetention
{
    public FireSweepResult Sweep(DateOnly today) => FireRetention.Sweep(dataDir, today);
}
