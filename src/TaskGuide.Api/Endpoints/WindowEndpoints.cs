namespace TaskGuide.Api.Endpoints;

/// <summary>
/// Windows within a Day template. Per-day instances, not shared definitions, so editing one never
/// propagates — the opposite rule from the template itself.
/// </summary>
public static class WindowEndpoints
{
    public static RouteGroupBuilder MapWindowEndpoints(this RouteGroupBuilder api)
    {
        var templates = api.MapGroup("/day-templates").WithTags("Schedule");

        // Windows are per-day instances, not shared definitions; editing one never propagates.
        templates.MapPost("/{id}/windows", (string id) => Results.NoContent());
        templates.MapPatch("/{id}/windows/{windowId}", (string id, string windowId) => Results.NoContent());
        templates.MapDelete("/{id}/windows/{windowId}", (string id, string windowId) => Results.NoContent());

        // The inverse warning, shown before a Dimension value is removed: "N Tasks depend on this
        // and nothing else declares it" — catching Drift at the edit that causes it.
        templates.MapGet("/{id}/windows/{windowId}/dependents", (string id, string windowId) => Results.NoContent());

        return api;
    }
}
