using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Notifications;
using TaskGuide.Domain.Tasks;

namespace TaskGuide.Application.Firing;

/// <summary>Builds the immutable notification payload from facts frozen during planning.</summary>
public static class ReminderComposer
{
    public static Reminder Compose(
        FireIntentKind kind,
        IReadOnlyList<TaskItem> shortlist,
        IReadOnlyList<EventLine> events,
        FooterCounts footer,
        IReadOnlyList<DimensionId> failedFetches,
        Uri landingPage,
        DateTimeOffset timeToLive)
    {
        var visible = shortlist.Take(3).ToArray();
        var top = visible[0];
        var duration = top.Tags.SingleOn(KnownDimensions.Duration)?.Value ?? "";

        return new Reminder(
            $"{top.Title} ({duration})",
            WindowContext(kind),
            visible,
            shortlist.Count - visible.Length,
            events,
            footer,
            failedFetches,
            landingPage,
            timeToLive);
    }

    private static string WindowContext(FireIntentKind kind) => kind.Match(
        window => window.Window.Window.Name,
        snooze => snooze.Window.Window.Name,
        _ => "",
        _ => "",
        _ => "");
}
