using TaskGuide.Domain.Notifications;

namespace TaskGuide.Application.Ports;

public interface IPushoverClient
{
    /// <summary>
    /// <c>firedAt</c> is written <b>only when Pushover accepts the message</b>, so a failed push
    /// reads as still-unfired on the next tick and is simply retried — bounded by the rules
    /// already in force. At-least-once, chosen to protect the biconditional: a transient blip
    /// must not quietly turn silence into "nothing fit OR the wifi hiccuped".
    /// </summary>
    Task<bool> SendReminderAsync(Reminder reminder, CancellationToken cancellationToken);

    /// <summary>Fire-and-forget: one attempt, failure logged, never retried.</summary>
    Task SendReceiptAsync(Receipt receipt, CancellationToken cancellationToken);

    /// <summary>A separate endpoint updating widget data only; nothing reaches Notification Center.</summary>
    Task<bool> SendGlanceAsync(Glance glance, CancellationToken cancellationToken);
}
