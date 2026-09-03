namespace TaskGuide.Api.Endpoints;

/// <summary>
/// Patterns — the weekday-to-Day-template mapping, and the single active one.
/// </summary>
public static class PatternEndpoints
{
    public static RouteGroupBuilder MapPatternEndpoints(this RouteGroupBuilder api)
    {
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

        return api;
    }
}
