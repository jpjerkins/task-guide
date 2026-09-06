using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Tasks;
using TaskGuide.Domain.Notifications;

namespace TaskGuide.Application.Firing;

public sealed record FireIntent(
    FireIntentKind Kind,
    IReadOnlyList<TaskItem> Shortlist,
    Reminder Reminder,
    FireRow FireRow);
