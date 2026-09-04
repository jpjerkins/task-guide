using TaskGuide.Application.Firing;

namespace TaskGuide.TestSupport;

/// <summary>Records the <c>now</c> of every <see cref="TickAsync"/> call.</summary>
public sealed class RecordingTickLoop : ITickLoop
{
    public List<DateTimeOffset> Ticks { get; } = [];

    public Task TickAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        Ticks.Add(now);
        return Task.CompletedTask;
    }
}
