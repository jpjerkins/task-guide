using TaskGuide.Application.Ports;

namespace TaskGuide.TestSupport;

/// <summary>
/// Records which of <see cref="AssertRegistry"/>, <see cref="SnapshotAsync"/>,
/// <see cref="MigrateAsync"/>, <see cref="SweepRegistryAsync"/> ran, in order — the sequence is
/// assert → snapshot → migrate → sweep → serve, so order is the thing worth asserting.
/// </summary>
public sealed class RecordingStartupSequence : IStartupSequence
{
    public List<string> Phases { get; } = [];

    public void AssertRegistry() => Phases.Add(nameof(AssertRegistry));

    public Task SnapshotAsync(CancellationToken cancellationToken)
    {
        Phases.Add(nameof(SnapshotAsync));
        return Task.CompletedTask;
    }

    public Task MigrateAsync(CancellationToken cancellationToken)
    {
        Phases.Add(nameof(MigrateAsync));
        return Task.CompletedTask;
    }

    public Task SweepRegistryAsync(CancellationToken cancellationToken)
    {
        Phases.Add(nameof(SweepRegistryAsync));
        return Task.CompletedTask;
    }
}
