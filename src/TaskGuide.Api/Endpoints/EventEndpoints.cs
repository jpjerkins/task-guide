using Microsoft.AspNetCore.Http.HttpResults;
using TaskGuide.Application.Ports;
using TaskGuide.Application.Schedule;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;

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
        events.MapGet("/overlap-check", OverlapCheck);
        events.MapPost("/", CreateAsync);
        events.MapPatch("/{id}", (string id) => Results.NoContent());
        events.MapDelete("/{id}", (string id) => Results.NoContent());

        // Keyed (date, prototypeId). Covers EDIT as well as delete: a deleted instance is absence
        // and a moved one is not, and expressing a move as delete-plus-create would silently
        // change whether the Absence rule fires. Deleting an instance does NOT stamp an Override.
        var exceptions = api.MapGroup("/event-exceptions").WithTags("Events");
        exceptions.MapGet("/", () => Results.NoContent());
        exceptions.MapPut("/{date}/{prototypeId}", EditExceptionAsync);
        exceptions.MapDelete("/{date}/{prototypeId}", DeleteExceptionAsync);

        return events;
    }

    private static Results<Ok<IReadOnlyList<AvailabilityWindow>>, BadRequest<object>> OverlapCheck(
        string? date, string? start, string? end, IStore store)
    {
        if (!DateOnly.TryParse(date, out var eventDate) || !TimeOnly.TryParse(start, out var eventStart) ||
            !TimeOnly.TryParse(end, out var eventEnd) || eventEnd <= eventStart)
        {
            return TypedResults.BadRequest<object>(new { error = "a date and ordered start/end times are required" });
        }

        return TypedResults.Ok<IReadOnlyList<AvailabilityWindow>>([.. EventScheduling.WindowsOn(store.Read(), eventDate)
            .Where(window => window.Start < eventEnd && eventStart < window.End)]);
    }

    private static async Task<Results<Created<Event>, BadRequest<object>, Conflict<object>>> CreateAsync(
        CreateEventRequest request, IStore store, IIdMinter minter, CancellationToken ct)
    {
        if (!DateOnly.TryParse(request.Date, out var date) || !TimeOnly.TryParse(request.Start, out var start) ||
            !TimeOnly.TryParse(request.End, out var end) || end <= start || string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.BadRequest<object>(new { error = "a date, name, and ordered start/end times are required" });
        }

        var resolutionRequests = request.Resolutions ?? [];
        if (resolutionRequests.Any(resolution => !TryParseOverlapResolution(resolution.Resolution, out _)) ||
            resolutionRequests.Select(resolution => resolution.WindowId).Distinct(StringComparer.Ordinal).Count() != resolutionRequests.Count)
        {
            return TypedResults.BadRequest<object>(new { error = "each overlap resolution must be replace, truncateStart, truncateEnd, or split" });
        }

        var resolutions = resolutionRequests.ToDictionary(
            resolution => new WindowId(resolution.WindowId),
            resolution => TryParseOverlapResolution(resolution.Resolution, out var parsed)
                ? parsed
                : throw new InvalidOperationException("Overlap resolutions are validated before they are applied."));
        var @event = new Event(minter.NextEventId(), date, request.Name, start, end, request.Tags ?? TagSet.Empty, request.AbsenceNotice);
        var outcome = await new CreateEvent(store, minter).ExecuteAsync(@event, resolutions, ct);

        return outcome.Match<Results<Created<Event>, BadRequest<object>, Conflict<object>>>(
            created => TypedResults.Created($"/api/events/{created.Event.Id.Value}", created.Event),
            refusal => TypedResults.Conflict<object>(new { error = refusal.Reason }));
    }

    private static async Task<Results<NoContent, BadRequest<object>>> EditExceptionAsync(
        string date, string prototypeId, EditEventExceptionRequest request, IStore store, CancellationToken ct)
    {
        if (!DateOnly.TryParse(date, out var exceptionDate) || !IsEventPrototypeId(prototypeId) ||
            (request.Deleted == false && request.Name is null && request.Start is null && request.End is null))
        {
            return TypedResults.BadRequest<object>(new { error = "a date, Event prototype id, and delete or edit are required" });
        }

        if ((request.Start is not null && !TimeOnly.TryParse(request.Start, out _)) ||
            (request.End is not null && !TimeOnly.TryParse(request.End, out _)))
        {
            return TypedResults.BadRequest<object>(new { error = "event exception times must be valid" });
        }

        await new EditEventException(store).ExecuteAsync(
            new EventException(exceptionDate, new EventPrototypeId(prototypeId), request.Deleted,
                request.Name, request.Start is null ? null : TimeOnly.Parse(request.Start), request.End is null ? null : TimeOnly.Parse(request.End)), ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, BadRequest<object>>> DeleteExceptionAsync(
        string date, string prototypeId, IStore store, CancellationToken ct)
    {
        if (!DateOnly.TryParse(date, out var exceptionDate) || !IsEventPrototypeId(prototypeId))
        {
            return TypedResults.BadRequest<object>(new { error = "a date and Event prototype id are required" });
        }

        await new DeleteEventException(store).ExecuteAsync(exceptionDate, new EventPrototypeId(prototypeId), ct);
        return TypedResults.NoContent();
    }

    private static bool IsEventPrototypeId(string value) => value.StartsWith(EventPrototypeId.Prefix, StringComparison.Ordinal);

    private static bool TryParseOverlapResolution(string value, out OverlapResolution resolution) =>
        Enum.TryParse(value, ignoreCase: true, out resolution) && Enum.IsDefined(resolution);
}

public sealed record CreateEventRequest(
    string Date,
    string Name,
    string Start,
    string End,
    TagSet? Tags,
    Offset? AbsenceNotice,
    IReadOnlyList<EventOverlapResolutionRequest>? Resolutions);

public sealed record EventOverlapResolutionRequest(string WindowId, string Resolution);
public sealed record EditEventExceptionRequest(bool Deleted, string? Name, string? Start, string? End);
