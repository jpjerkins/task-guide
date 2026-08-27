namespace TaskGuide.Api.Endpoints;

/// <summary>
/// "Right now" on demand — the home tab, and the on-demand sibling of a Reminder. Deliberately a
/// separate thing from a Snooze re-fire, which re-matches against its <em>original</em> Window.
/// </summary>
public static class RightNowEndpoints
{
    public static RouteGroupBuilder MapRightNowEndpoints(this RouteGroupBuilder api)
    {
        var now = api.MapGroup("/right-now").WithTags("Right now");

        now.MapGet("/", () => Results.NoContent());

        // "Matching on" — adjusting the query at the moment. It WRITES STRAIGHT THROUGH: no
        // accept step, no Reset, because an adjustment is a statement about the day you are
        // living. It edits that date's copy of the Window, which is what an Override already is,
        // and the chips are their own undo. No stacking — the first adjustment materialises the
        // date and detaches it from its Day template.
        now.MapPut("/matching-on", () => Results.NoContent());

        // Between Windows this mints an ad-hoc Window on that date, running from now to the next
        // commitment. It never fires — you are already looking at it.
        now.MapPost("/ad-hoc-window", () => Results.NoContent());

        return now;
    }
}
