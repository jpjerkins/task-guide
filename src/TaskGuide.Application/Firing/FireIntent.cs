using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Tasks;

namespace TaskGuide.Application.Firing;

public sealed record FireIntent(
    FireIntentKind Kind,
    IReadOnlyList<TaskItem> Shortlist,
    DateTimeOffset TimeToLive,
    FireRow FireRow);
