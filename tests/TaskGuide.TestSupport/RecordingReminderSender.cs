using TaskGuide.Application.Ports;
using TaskGuide.Domain.Notifications;

namespace TaskGuide.TestSupport;

/// <summary>
/// Records every <see cref="Reminder"/> it was handed and reports a configurable outcome,
/// defaulting to success. Never throws — <see cref="IReminderSender"/>'s contract is that
/// adapters return an outcome rather than throw for an expected failure, and a fake that threw
/// would train callers to wrap it in a <c>try</c>.
/// </summary>
public sealed class RecordingReminderSender : IReminderSender
{
    public List<Reminder> Reminders { get; } = [];
    private bool _failNext;

    /// <summary>Makes the next <see cref="SendReminderAsync"/> call return <c>false</c>.</summary>
    public void FailNextSend() => _failNext = true;

    public Task<bool> SendReminderAsync(Reminder reminder, CancellationToken cancellationToken)
    {
        Reminders.Add(reminder);
        var succeeded = !_failNext;
        _failNext = false;
        return Task.FromResult(succeeded);
    }
}
