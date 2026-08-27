namespace TaskGuide.Api.Endpoints;

/// <summary>
/// Day templates, Patterns and Overrides — the shapes, and the dated realities layered on them.
/// <b>Everything under /day-templates and /patterns edits shapes, so it propagates; everything
/// under /overrides edits one date.</b> The two are different verbs, which is why they are
/// separate groups rather than one schedule surface.
/// </summary>
public static class ScheduleEndpoints
{
    public static RouteGroupBuilder MapScheduleEndpoints(this RouteGroupBuilder api)
    {
        var templates = api.MapGroup("/day-templates").WithTags("Schedule");
        templates.MapGet("/", () => Results.NoContent());
        templates.MapGet("/{id}", (string id) => Results.NoContent());
        templates.MapPost("/", () => Results.NoContent());
        templates.MapPatch("/{id}", (string id) => Results.NoContent());

        // Gated on `Unused` — derived, never stored. Reachable from nothing, so the delete cannot
        // corrupt any record and the dangerous case is unrepresentable rather than warned about.
        // The confirmation names any Event prototypes carried, since those hold Absence notices.
        templates.MapDelete("/{id}", (string id) => Results.NoContent());

        // Shown BEFORE saving an edit: "used by 3 Patterns: Volleyball, Summer, School year".
        // Blast radius is made visible, not prevented.
        templates.MapGet("/{id}/usage", (string id) => Results.NoContent());

        // Windows are per-day instances, not shared definitions; editing one never propagates.
        templates.MapPost("/{id}/windows", (string id) => Results.NoContent());
        templates.MapPatch("/{id}/windows/{windowId}", (string id, string windowId) => Results.NoContent());
        templates.MapDelete("/{id}/windows/{windowId}", (string id, string windowId) => Results.NoContent());

        // The inverse warning, shown before a Dimension value is removed: "N Tasks depend on this
        // and nothing else declares it" — catching Drift at the edit that causes it.
        templates.MapGet("/{id}/windows/{windowId}/dependents", (string id, string windowId) => Results.NoContent());

        templates.MapPost("/{id}/event-prototypes", (string id) => Results.NoContent());
        templates.MapPatch("/{id}/event-prototypes/{prototypeId}", (string id, string prototypeId) => Results.NoContent());
        templates.MapDelete("/{id}/event-prototypes/{prototypeId}", (string id, string prototypeId) => Results.NoContent());

        var patterns = api.MapGroup("/patterns").WithTags("Schedule");
        patterns.MapGet("/", () => Results.NoContent());
        patterns.MapPost("/", () => Results.NoContent());
        patterns.MapPatch("/{id}", (string id) => Results.NoContent());

        // Any Pattern that is not the active one, confirmed by name. The confirmation deliberately
        // does NOT report which Day templates the deletion strands.
        patterns.MapDelete("/{id}", (string id) => Results.NoContent());

        // Switching can orphan a whole class of Tasks at once, so the count comes UP FRONT.
        patterns.MapGet("/active/switch-impact", () => Results.NoContent());
        patterns.MapPut("/active", () => Results.NoContent());

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

        // The computed shape of any date: Override[date] ?? Pattern[weekday], Events layered on.
        // A read of a day's shape must never write one.
        var days = api.MapGroup("/days").WithTags("Schedule");
        days.MapGet("/{date}", (string date) => Results.NoContent());

        return api;
    }
}
