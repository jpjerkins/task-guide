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

    /// <summary>
    /// <b>Up to three attempts, and only while Pushover has not accepted</b> — a refused
    /// connection, a timeout, or a 5xx. An accepted-then-lost response is never retried, because
    /// that is the one case a second attempt could re-notify something already dismissed; a 4xx
    /// is never retried because it fails the same way three times. Roughly 3s per attempt with a
    /// short backoff, awaited on the capture's response. Failure is logged and never escalated.
    /// </summary>
    Task SendReceiptAsync(Receipt receipt, CancellationToken cancellationToken);

    /// <summary>A separate endpoint updating widget data only; nothing reaches Notification Center.</summary>
    Task<bool> SendGlanceAsync(Glance glance, CancellationToken cancellationToken);
}
