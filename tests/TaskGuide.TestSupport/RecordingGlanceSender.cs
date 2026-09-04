using TaskGuide.Application.Ports;
using TaskGuide.Domain.Notifications;

namespace TaskGuide.TestSupport;

/// <summary>
/// Records every <see cref="GlanceState"/> it was handed and reports a configurable outcome,
/// defaulting to success. Never throws, for the same reason as <see cref="RecordingReceiptSender"/>
/// and <see cref="RecordingReminderSender"/> — adapters return an outcome, they do not throw.
/// </summary>
public sealed class RecordingGlanceSender : IGlanceSender
{
    public List<GlanceState> Sent { get; } = [];
    private bool _failNext;

    /// <summary>Makes the next <see cref="SendGlanceAsync"/> call return <c>false</c>.</summary>
    public void FailNextSend() => _failNext = true;

    public Task<bool> SendGlanceAsync(GlanceState state, CancellationToken cancellationToken)
    {
        Sent.Add(state);
        var succeeded = !_failNext;
        _failNext = false;
        return Task.FromResult(succeeded);
    }
}
