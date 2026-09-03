using TaskGuide.Domain.Notifications;

namespace TaskGuide.Application.Ports;

public interface IReceiptSender
{
    /// <summary>
    /// <b>Up to three attempts, and only while Pushover has not accepted</b> — a refused
    /// connection, a timeout, or a 5xx. An accepted-then-lost response is never retried, because
    /// that is the one case a second attempt could re-notify something already dismissed; a 4xx
    /// is never retried because it fails the same way three times. Roughly 3s per attempt with a
    /// short backoff, awaited on the capture's response.
    /// <b>Adapters never throw for expected failures; they return an outcome</b> (#69) —
    /// "logged, never retried" beyond that point is the caller's policy, not something the
    /// adapter hides.
    /// </summary>
    Task<bool> SendReceiptAsync(Receipt receipt, CancellationToken cancellationToken);
}
