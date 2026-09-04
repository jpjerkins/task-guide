using TaskGuide.Application.Ports;

namespace TaskGuide.TestSupport;

/// <summary>Records every <see cref="RecordTick"/> instant, in order.</summary>
public sealed class RecordingTickHeartbeat : ITickHeartbeat
{
    public List<DateTimeOffset> Ticks { get; } = [];

    public void RecordTick(DateTimeOffset at) => Ticks.Add(at);
}
