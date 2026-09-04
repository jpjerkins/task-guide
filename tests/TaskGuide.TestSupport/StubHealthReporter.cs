using TaskGuide.Application.Ports;

namespace TaskGuide.TestSupport;

/// <summary>Returns a configurable <see cref="HealthReport"/>, defaulting to a healthy one.</summary>
public sealed class StubHealthReporter : IHealthReporter
{
    private HealthReport _report = new(
        Ok: true,
        LastTick: DateTimeOffset.UtcNow,
        Storage: new StorageHealth(Readable: true, Writable: null),
        Uptime: TimeSpan.FromMinutes(5));

    public void SetReport(HealthReport report) => _report = report;

    public HealthReport Current() => _report;
}
