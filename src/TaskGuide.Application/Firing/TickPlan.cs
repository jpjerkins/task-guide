using TaskGuide.Domain.Notifications;

namespace TaskGuide.Application.Firing;

public sealed record TickPlan(
    IReadOnlyList<FireIntent> Fires,
    GlanceState? Glance);
