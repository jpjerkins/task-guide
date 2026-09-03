using TaskGuide.Domain.Notifications;

namespace TaskGuide.Application.Ports;

public interface IGlanceSender
{
    /// <summary>A separate endpoint updating widget data only; nothing reaches Notification Center.</summary>
    Task<bool> SendGlanceAsync(GlanceState state, CancellationToken cancellationToken);
}
