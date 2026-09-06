using TaskGuide.Application.Ports;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Notifications;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Time;

namespace TaskGuide.Application.Firing;

public static class Fallback
{
    public static Event? CarrierFor(IStoreView view, DateOnly date) =>
        view.Events
            .Where(@event => @event.Date >= date && @event.Date <= date.AddDays(FiringPolicy.EventRunwayDays))
            .OrderBy(@event => @event.Date)
            .ThenBy(@event => @event.Start)
            .FirstOrDefault();

    public static IReadOnlyList<EventLine> EventsForFooter(
        IReadOnlyList<Event> todayEvents,
        IStoreView view,
        DateOnly date) =>
        todayEvents
            .Concat(view.Events.Where(@event => @event.Date > date && @event.Date <= date.AddDays(FiringPolicy.EventRunwayDays)))
            .OrderBy(@event => @event.Date)
            .ThenBy(@event => @event.Start)
            .Select(@event => new EventLine(@event.Id, @event.Name, @event.Date.DayOfWeek))
            .ToArray();

    public static bool IsCarried(DayFires fires) => fires.Rows.Any(row => row.IsFired);

    public static bool IsDue(
        Event? carrier,
        IReadOnlyList<AvailabilityWindow> windows,
        DayFires fires,
        DateTimeOffset now,
        ClockTimeResolution resolution,
        DayBoundary boundary)
    {
        var date = boundary.DateOf(now);
        return carrier is not null
            && !IsCarried(fires)
            && now >= resolution.Resolve(date, FiringPolicy.FallbackPushEarliest)
            && now < boundary.EndOf(date)
            && windows.All(window =>
            {
                var resolved = resolution.ResolveWindow(date, window);
                return resolved is null || resolved.End <= now;
            });
    }

    public static FireIntent Intent(
        Event carrier,
        IReadOnlyList<EventLine> events,
        FooterCounts footer,
        IReadOnlyList<DimensionId> failedFetches,
        Uri landingPage,
        DateTimeOffset dayBoundary) =>
        new(
            new FallbackFire(),
            [],
            new Reminder(carrier.Name, "", [], 0, events, footer, failedFetches, landingPage, dayBoundary),
            new FireRow(null, FireKind.Fallback, null, null, null, null, null, null, carrier.Id));
}
