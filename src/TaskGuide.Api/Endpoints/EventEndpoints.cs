namespace TaskGuide.Api.Endpoints;

/// <summary>
/// Dated Events, and the single-date exceptions to the recurring ones a prototype generates.
/// </summary>
public static class EventEndpoints
{
    public static RouteGroupBuilder MapEventEndpoints(this RouteGroupBuilder api)
    {
        var events = api.MapGroup("/events").WithTags("Events");

        events.MapGet("/", () => Results.NoContent());

        // Creating an Event that overlaps a Window — even partially — prompts for replace /
        // truncate-start / truncate-end / split. One user action, one question, two artifacts:
        // the Event AND the one-off day it generates. The Event is written FIRST, so a crash
        // between the two leaves the condition the overlap check looks for and the store heals.
        events.MapGet("/overlap-check", () => Results.NoContent());
        events.MapPost("/", () => Results.NoContent());
        events.MapPatch("/{id}", (string id) => Results.NoContent());
        events.MapDelete("/{id}", (string id) => Results.NoContent());

        // Keyed (date, prototypeId). Covers EDIT as well as delete: a deleted instance is absence
        // and a moved one is not, and expressing a move as delete-plus-create would silently
        // change whether the Absence rule fires. Deleting an instance does NOT stamp an Override.
        var exceptions = api.MapGroup("/event-exceptions").WithTags("Events");
        exceptions.MapGet("/", () => Results.NoContent());
        exceptions.MapPut("/{date}/{prototypeId}", (string date, string prototypeId) => Results.NoContent());
        exceptions.MapDelete("/{date}/{prototypeId}", (string date, string prototypeId) => Results.NoContent());

        return events;
    }
}
