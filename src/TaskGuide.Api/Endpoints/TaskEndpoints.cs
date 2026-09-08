using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using OneOf;
using TaskGuide.Application.Ports;
using TaskGuide.Application.Rules;
using TaskGuide.Application.Tasks;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Time;
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
        tasks.MapGet("/", (
            string? status,
            IStore store,
            DimensionRegistry registry,
            StaleThresholds staleThresholds,
            TimeProvider timeProvider,
            DayBoundary boundary,
            DerivedTaskComposer derivedTasks) =>
        {
            var view = store.Read();
            var filtered = derivedTasks.Compose(view).AsEnumerable();
            if (Enum.TryParse<Status>(status, ignoreCase: true, out var requestedStatus))
            {
                var now = timeProvider.GetUtcNow();
                filtered = filtered.Where(task =>
                    StatusRules.Of(task, view.CompletionsFor(task.Id), registry, staleThresholds, now, boundary) == requestedStatus);
            }

            return TypedResults.Ok(filtered.Select(ToResponse));
        });

        tasks.MapPost("/", async Task<Results<Created<TaskResponse>, BadRequest<object>, ProblemHttpResult>> (CreateTaskRequest request, IStore store, IIdMinter minter, ILogger<TaskEndpointsLogCategory> logger, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return TypedResults.BadRequest<object>(new { error = "title is required" });
            }

            if (request.Duration <= 0)
            {
                return TypedResults.BadRequest<object>(new { error = "duration must be a positive integer" });
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

            try
            {
                await store.MutateAsync<Never>(
                    view => OneOf<StoreMutation, Never>.FromT0(new StoreMutation([new TasksWrite((IReadOnlyList<TaskItem>)[.. view.Tasks, task])])),
                    ct);
            }
            catch (Exception ex)
            {
                // A failed persist is a deliberate 503, never a raw 500 — the disk rejecting a
                // write is an infrastructure condition the caller can retry, not a server bug.
                logger.LogError(ex, "Failed to persist Task {TaskId}", task.Id);
                return TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Storage is temporarily unavailable");
            }

            return TypedResults.Created($"/api/tasks/{task.Id.Value}", ToResponse(task));
        })
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // ?status=unprocessed|stale|active|done|orphan — Status is derived per request, never read
        // from storage. `orphan` is a third, disjoint filter, not a Status.
        tasks.MapGet("/{id}", (string id) => Results.NoContent());
        // An absolute date is for a one-off Task; recurring Tasks carry a moving, per-instance
        // offset from their generated deadline instead.
        tasks.MapPatch("/{id}", async Task<IResult> (string id, DeferTaskRequest request, IStore store, DerivedTaskComposer derivedTasks, CancellationToken ct) =>
        {
            if (!IsTaskId(id))
            {
                return TypedResults.BadRequest(new { error = "id must be a Task id" });
            }

            var defer = ToDefer(request);
            if (defer is null)
            {
                return TypedResults.BadRequest(new { error = "supply either date or offset and unit" });
            }

            var result = await new DeferTask(store, derivedTasks).ExecuteAsync(new TaskId(id), defer, ct);
            return result.Match<IResult>(
                _ => TypedResults.NoContent(),
                refusal => TypedResults.Conflict(new { error = refusal.Reason }));
        });
        tasks.MapDelete("/{id}", (string id) => Results.NoContent());

        // The only authored completion fact. Refused on an `Unprocessed` Task — there is nothing
        // yet to be done within — and on a derived Task it is the only interaction there is.
        tasks.MapPost("/{id}/completions", async Task<IResult> (
            string id,
            IStore store,
            DimensionRegistry registry,
            StaleThresholds staleThresholds,
            TimeProvider timeProvider,
            DayBoundary boundary,
            DerivedTaskComposer derivedTasks,
            CancellationToken ct) =>
        {
            if (!IsTaskId(id))
            {
                return TypedResults.BadRequest(new { error = "id must be a Task id" });
            }

            var result = await new CompleteTask(store, registry, staleThresholds, timeProvider, boundary, derivedTasks)
                .ExecuteAsync(new TaskId(id), ct);
            return result.Match<IResult>(
                _ => TypedResults.NoContent(),
                refusal => TypedResults.Conflict(new { error = refusal.Reason }));
        });
        tasks.MapDelete("/{id}/completions/{due}", (string id, string due) => Results.NoContent());

        // "Not now." Stored as an absolute date; "two weeks" is a UI shorthand resolved at write
        // time. Offered on Active rows only — never on recurring or derived Tasks.
        tasks.MapPut("/{id}/postpone", async Task<IResult> (string id, PostponeTaskRequest request, IStore store, DerivedTaskComposer derivedTasks, CancellationToken ct) =>
        {
            if (!IsTaskId(id))
            {
                return TypedResults.BadRequest(new { error = "id must be a Task id" });
            }

            var result = await new PostponeTask(store, derivedTasks).ExecuteAsync(new TaskId(id), request.Date, ct);
            return result.Match<IResult>(
                _ => TypedResults.NoContent(),
                refusal => TypedResults.Conflict(new { error = refusal.Reason }));
        });
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

    private static bool IsTaskId(string id) =>
        id.Length == 28 && id.StartsWith(TaskId.Prefix, StringComparison.Ordinal) && id[2..].All(character => "0123456789ABCDEFGHJKMNPQRSTVWXYZ".Contains(character));

    private static Defer? ToDefer(DeferTaskRequest request) =>
        request switch
        {
            { Date: { } date, Offset: null, Unit: null } => new AbsoluteDefer(date),
            { Date: null, Offset: > 0, Unit: { } unit } => new OffsetDefer(new BeforeOffset(request.Offset.Value, unit)),
            _ => null,
        };
}

/// <summary>Walking skeleton request shape (#51): a Task is a title and a Duration, nothing else.</summary>
public sealed record CreateTaskRequest(string Title, int Duration);

public sealed record TaskResponse(string Id, string Title, int? Duration, DateTimeOffset CreatedAt);

public sealed record PostponeTaskRequest(DateOnly Date);

public sealed record DeferTaskRequest(DateOnly? Date, int? Offset, OffsetUnit? Unit);

/// <summary>A logging-category marker — <see cref="TaskEndpoints"/> is static and can't be used as one directly.</summary>
public sealed class TaskEndpointsLogCategory;
