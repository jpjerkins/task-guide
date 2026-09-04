using TaskGuide.Domain.Notifications;

namespace TaskGuide.Application.Ports;

public interface IReminderSender
{
    /// <summary>
    /// <c>firedAt</c> is written <b>only when Pushover accepts the message</b>, so a failed push
    /// reads as still-unfired on the next tick and is simply retried — bounded by the rules
    /// already in force. At-least-once, chosen to protect the biconditional: a transient blip
    /// must not quietly turn silence into "nothing fit OR the wifi hiccuped".
    /// </summary>
    Task<bool> SendReminderAsync(Reminder reminder, CancellationToken cancellationToken);
}
