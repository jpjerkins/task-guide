namespace TaskGuide.Api.Endpoints;

/// <summary>
/// The computed shape of a single date: Override[date] ?? Pattern[weekday], Events layered on.
/// A read of a day's shape must never write one.
/// </summary>
public static class DayEndpoints
{
    public static RouteGroupBuilder MapDayEndpoints(this RouteGroupBuilder api)
    {
        var days = api.MapGroup("/days").WithTags("Schedule");
        days.MapGet("/{date}", (string date) => Results.NoContent());

        return api;
    }
}
