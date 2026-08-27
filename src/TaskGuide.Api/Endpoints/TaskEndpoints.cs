namespace TaskGuide.Api.Endpoints;

/// <summary>
/// Task list (+ status filters), task detail, and the two reactive gestures.
/// Standing requirement: <b>everything doable via the API must also be doable through the UI.</b>
/// </summary>
public static class TaskEndpoints
{
    public static RouteGroupBuilder MapTaskEndpoints(this RouteGroupBuilder api)
    {
        var tasks = api.MapGroup("/tasks").WithTags("Tasks");

        // ?status=unprocessed|stale|active|done|orphan — Status is derived per request, never read
        // from storage. `orphan` is a third, disjoint filter, not a Status.
        tasks.MapGet("/", () => Results.NoContent());
        tasks.MapGet("/{id}", (string id) => Results.NoContent());
        tasks.MapPost("/", () => Results.NoContent());
        tasks.MapPatch("/{id}", (string id) => Results.NoContent());
        tasks.MapDelete("/{id}", (string id) => Results.NoContent());

        // The only authored completion fact. Refused on an `Unprocessed` Task — there is nothing
        // yet to be done within — and on a derived Task it is the only interaction there is.
        tasks.MapPost("/{id}/completions", (string id) => Results.NoContent());
        tasks.MapDelete("/{id}/completions/{due}", (string id, string due) => Results.NoContent());

        // "Not now." Stored as an absolute date; "two weeks" is a UI shorthand resolved at write
        // time. Offered on Active rows only — never on recurring or derived Tasks.
        tasks.MapPut("/{id}/postpone", (string id) => Results.NoContent());
        tasks.MapDelete("/{id}/postpone", (string id) => Results.NoContent());

        // The Orphan badge's deep link: the active Pattern's distinct Day templates that don't yet
        // declare a value on this Task's unmatched Dimension.
        tasks.MapGet("/{id}/orphan-repair", (string id) => Results.NoContent());

        return tasks;
    }
}
