namespace TaskGuide.Api.Endpoints;

/// <summary>
/// Overrides — dated realities layered on the computed shape. <b>Everything under /overrides
/// edits one date</b>, the opposite verb from /day-templates and /patterns, which is why it is a
/// separate group rather than folded into the shape-editing surface.
/// </summary>
public static class OverrideEndpoints
{
    public static RouteGroupBuilder MapOverrideEndpoints(this RouteGroupBuilder api)
    {
        var overrides = api.MapGroup("/overrides").WithTags("Schedule");

        // Sparse and read by RANGE — the 3-day runway, the 7-day horizon.
        overrides.MapGet("/", () => Results.NoContent());
        overrides.MapGet("/{date}", (string date) => Results.NoContent());

        // ONE AUTHORING GESTURE, a start–end range, writing one Override per date — each
        // independently editable afterwards (#41). A range landing on already-overridden dates is
        // a replacement, confirmed in one batch before the write.
        overrides.MapPost("/", () => Results.NoContent());
        overrides.MapGet("/clobber-check", () => Results.NoContent());

        // Applying a named template is a STAMP, not a link, and the copy preserves each Window's
        // id — load-bearing for the Fire record when a date materialises mid-day.
        overrides.MapPut("/{date}/stamp", (string date) => Results.NoContent());

        // Editing a stamped date directly makes it a one-off day; the use record survives that.
        overrides.MapPatch("/{date}", (string date) => Results.NoContent());
        overrides.MapDelete("/{date}", (string date) => Results.NoContent());

        // Promotion copies the shape OUTWARD; the source date keeps its own copy and does not
        // re-link, but it DOES get a use record — otherwise "keep this" produces something born
        // `Unused`, and the Christmas Day case sits deletable for eleven months.
        overrides.MapPost("/{date}/promote", (string date) => Results.NoContent());

        return api;
    }
}
