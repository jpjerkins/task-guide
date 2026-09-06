using TaskGuide.Application.Ports;
using TaskGuide.Domain.Notifications;

namespace TaskGuide.Application.Firing;

/// <summary>
/// Schedules the disposable Glance readout in executor memory. A restart deliberately loses this
/// state: one early update is preferable to a complication that appears dead.
/// </summary>
public sealed class GlanceScheduling(IGlanceSender sender)
{
    private static readonly TimeSpan Floor = TimeSpan.FromMinutes(30);
    private readonly IGlanceSender _sender = sender;
    private GlanceState? _lastSent;
    private DateTimeOffset? _lastSentAt;
    private bool _retryPending;
    private GlanceState? _retryExhausted;

    public async Task SendAsync(GlanceState next, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var retrying = _retryPending;
        if (!retrying && (_retryExhausted?.Equals(next) is true || !ShouldSend(next, now)))
        {
            return;
        }

        var accepted = await _sender.SendGlanceAsync(next, cancellationToken);
        if (accepted)
        {
            _lastSent = next;
            _lastSentAt = now;
            _retryPending = false;
            _retryExhausted = null;
            return;
        }

        if (retrying)
        {
            _retryPending = false;
            _retryExhausted = next;
            return;
        }

        _retryPending = true;
    }

    private bool ShouldSend(GlanceState next, DateTimeOffset now) =>
        _lastSentAt is null
        || GlancePolicy.ShouldSend(
            next,
            _lastSent,
            now - _lastSentAt.Value,
            WindowJustStarted(next),
            Floor);

    private bool WindowJustStarted(GlanceState next) =>
        next.Shape.Value is InsideWindow inside
        && (_lastSent?.Shape.Value is not InsideWindow previous || !inside.Window.Equals(previous.Window));
}
