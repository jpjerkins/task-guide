using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;

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

        // Walking skeleton slice (#51): a Task is a title and a Duration. No matching, ranking,
        // Recurrence or Dimensions beyond the one (Duration) the skeleton needs to prove the
        // substrate end to end.
        tasks.MapGet("/", (IStore store) =>
            Results.Ok(store.Read().Tasks.Select(ToResponse)));

        tasks.MapPost("/", async (CreateTaskRequest request, IStore store, IIdMinter minter, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return Results.BadRequest(new { error = "title is required" });
            }

            if (request.Duration <= 0)
            {
                return Results.BadRequest(new { error = "duration must be a positive integer" });
            }

            var task = new TaskItem(
                minter.NextTaskId(),
                request.Title,
                Notes: null,
                new TagSet(
                    new Dictionary<DimensionId, IReadOnlyList<TagValue>>
                    {
                        [KnownDimensions.Duration] = [new TagValue(request.Duration.ToString())],
                    },
                    LooseTags: []),
                Deadline: null,
                Defer: null,
                Postpone: null,
                Recurrence: null,
                DateTimeOffset.UtcNow);

            await store.MutateAsync(
                view => new StoreMutation([(IReadOnlyList<TaskItem>)[.. view.Tasks, task]]),
                ct);

            return Results.Created($"/api/tasks/{task.Id.Value}", ToResponse(task));
        });

        // ?status=unprocessed|stale|active|done|orphan — Status is derived per request, never read
        // from storage. `orphan` is a third, disjoint filter, not a Status.
        tasks.MapGet("/{id}", (string id) => Results.NoContent());
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

    private static TaskResponse ToResponse(TaskItem task) => new(
        task.Id.Value,
        task.Title,
        DurationOf(task),
        task.CreatedAt);

    /// <summary>The one Dimension the walking skeleton (#51) reads back out — the ordinal single value, if any.</summary>
    private static int? DurationOf(TaskItem task) =>
        task.Tags.SingleOn(KnownDimensions.Duration) is { } duration ? int.Parse(duration.Value) : null;
}

/// <summary>Walking skeleton request shape (#51): a Task is a title and a Duration, nothing else.</summary>
public sealed record CreateTaskRequest(string Title, int Duration);

public sealed record TaskResponse(string Id, string Title, int? Duration, DateTimeOffset CreatedAt);
