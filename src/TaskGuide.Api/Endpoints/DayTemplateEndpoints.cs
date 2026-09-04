namespace TaskGuide.Api.Endpoints;

/// <summary>
/// Day templates — reusable shapes. <b>Everything under /day-templates edits a shape, so it
/// propagates</b> to every date the shape is stamped onto, which is why usage and dependents are
/// surfaced before a destructive edit rather than after.
/// </summary>
public static class DayTemplateEndpoints
{
    public static RouteGroupBuilder MapDayTemplateEndpoints(this RouteGroupBuilder api)
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

        templates.MapPost("/{id}/event-prototypes", (string id) => Results.NoContent());
        templates.MapPatch("/{id}/event-prototypes/{prototypeId}", (string id, string prototypeId) => Results.NoContent());
        templates.MapDelete("/{id}/event-prototypes/{prototypeId}", (string id, string prototypeId) => Results.NoContent());

        return api;
    }
}
