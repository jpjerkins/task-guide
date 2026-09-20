using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using OneOf;
using TaskGuide.Application.Ports;
using TaskGuide.Application.Rules;
using TaskGuide.Application.Tasks;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Matching;
using TaskGuide.Domain.Ranking;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Time;
using TaskGuide.Domain.Tasks;

namespace TaskGuide.Api.Endpoints;

/// <summary>
/// Task list (+ status filters), task detail, and reactive gestures.
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
            IDayShapeReader shapes,
            ClockTimeResolution resolution,
            DerivedTaskComposer derivedTasks) =>
        {
            var view = store.Read();
            var now = timeProvider.GetUtcNow();
            var opportunities = new OpportunityCounter(shapes, registry, resolution, boundary);
            var responses = derivedTasks.Compose(view).Select(task => ToResponse(
                task,
                view,
                registry,
                staleThresholds,
                now,
                boundary,
                opportunities));

            if (string.Equals(status, "orphan", StringComparison.OrdinalIgnoreCase))
            {
                responses = responses.Where(task => task.ZeroKind == ToWireName(ZeroKind.Orphan));
            }
            else if (Enum.TryParse<Status>(status, ignoreCase: true, out var requestedStatus))
            {
                var requestedWireName = ToWireName(requestedStatus);
                responses = responses.Where(task => task.Status == requestedWireName);
            }

            return TypedResults.Ok(responses);
        });

        tasks.MapPost("/", async Task<Results<Created<TaskResponse>, BadRequest<object>, ProblemHttpResult>> (
            CreateTaskRequest request,
            IStore store,
            IIdMinter minter,
            ILogger<TaskEndpointsLogCategory> logger,
            DimensionRegistry registry,
            StaleThresholds staleThresholds,
            TimeProvider timeProvider,
            DayBoundary boundary,
            IDayShapeReader shapes,
            ClockTimeResolution resolution,
            CancellationToken ct) =>
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
                        [KnownDimensions.Duration] = [DurationCeiling.SnapUp(request.Duration, KnownDimensions.DurationBuckets)],
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

            var now = timeProvider.GetUtcNow();
            return TypedResults.Created(
                $"/api/tasks/{task.Id.Value}",
                ToResponse(
                    task,
                    store.Read(),
                    registry,
                    staleThresholds,
                    now,
                    boundary,
                    new OpportunityCounter(shapes, registry, resolution, boundary)));
        })
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // ?status=unprocessed|stale|active|done|orphan — Status is derived per request, never read
        // from storage. `orphan` is a third, disjoint filter, not a Status.
        tasks.MapGet("/{id}", Results<Ok<TaskResponse>, NotFound> (
            string id,
            IStore store,
            DimensionRegistry registry,
            StaleThresholds staleThresholds,
            TimeProvider timeProvider,
            DayBoundary boundary,
            IDayShapeReader shapes,
            ClockTimeResolution resolution,
            DerivedTaskComposer derivedTasks) =>
        {
            var view = store.Read();
            var task = derivedTasks.Compose(view).SingleOrDefault(task => task.Id.Value == id);
            if (task is null)
            {
                return TypedResults.NotFound();
            }

            return TypedResults.Ok(ToResponse(
                task,
                view,
                registry,
                staleThresholds,
                timeProvider.GetUtcNow(),
                boundary,
                new OpportunityCounter(shapes, registry, resolution, boundary)));
        });
        // An absolute date is for a one-off Task; recurring Tasks carry a moving, per-instance
        // offset from their generated deadline instead.
        tasks.MapPatch("/{id}", async Task<Results<NoContent, BadRequest<object>, Conflict<object>>> (string id, DeferTaskRequest request, IStore store, DerivedTaskComposer derivedTasks, CancellationToken ct) =>
        {
            if (!IsTaskId(id))
            {
                return TypedResults.BadRequest<object>(new { error = "id must be a Task id" });
            }

            var defer = ToDefer(request);
            if (defer is null)
            {
                return TypedResults.BadRequest<object>(new { error = "supply either date or offset and unit" });
            }

            var result = await new DeferTask(store, derivedTasks).ExecuteAsync(new TaskId(id), defer, ct);
            return result.Match<Results<NoContent, BadRequest<object>, Conflict<object>>>(
                _ => TypedResults.NoContent(),
                refusal => TypedResults.Conflict<object>(new { error = refusal.Reason }));
        });
        tasks.MapPut("/{id}/duration", async Task<Results<NoContent, BadRequest<object>, Conflict<object>>> (string id, SetTaskDurationRequest request, IStore store, CancellationToken ct) =>
        {
            if (!IsTaskId(id))
            {
                return TypedResults.BadRequest<object>(new { error = "id must be a Task id" });
            }

            var result = await new SetTaskDuration(store).ExecuteAsync(new TaskId(id), request.Duration, ct);
            return result.Match<Results<NoContent, BadRequest<object>, Conflict<object>>>(
                _ => TypedResults.NoContent(),
                invalid => TypedResults.BadRequest<object>(new { error = invalid.Reason }),
                refusal => TypedResults.Conflict<object>(new { error = refusal.Reason }));
        });
        tasks.MapDelete("/{id}", (string id) => Results.NoContent());

        // The only authored completion fact. Refused on an `Unprocessed` Task — there is nothing
        // yet to be done within — and on a derived Task it is the only interaction there is.
        tasks.MapPost("/{id}/completions", async Task<Results<NoContent, BadRequest<object>, Conflict<object>>> (
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
                return TypedResults.BadRequest<object>(new { error = "id must be a Task id" });
            }

            var result = await new CompleteTask(store, registry, staleThresholds, timeProvider, boundary, derivedTasks)
                .ExecuteAsync(new TaskId(id), ct);
            return result.Match<Results<NoContent, BadRequest<object>, Conflict<object>>>(
                _ => TypedResults.NoContent(),
                refusal => TypedResults.Conflict<object>(new { error = refusal.Reason }));
        });
        tasks.MapDelete("/{id}/completions/{due}", (string id, string due) => Results.NoContent());

        // "Not now." Stored as an absolute date; "two weeks" is a UI shorthand resolved at write
        // time. Offered on Active rows only — never on recurring or derived Tasks.
        tasks.MapPut("/{id}/postpone", async Task<Results<NoContent, BadRequest<object>, Conflict<object>>> (string id, PostponeTaskRequest request, IStore store, DerivedTaskComposer derivedTasks, CancellationToken ct) =>
        {
            if (!IsTaskId(id))
            {
                return TypedResults.BadRequest<object>(new { error = "id must be a Task id" });
            }

            var result = await new PostponeTask(store, derivedTasks).ExecuteAsync(new TaskId(id), request.Date, ct);
            return result.Match<Results<NoContent, BadRequest<object>, Conflict<object>>>(
                _ => TypedResults.NoContent(),
                refusal => TypedResults.Conflict<object>(new { error = refusal.Reason }));
        });
        tasks.MapDelete("/{id}/postpone", (string id) => Results.NoContent());


        // The Orphan badge's deep link: the active Pattern's distinct Day templates that don't yet
        // declare a value on this Task's unmatched Dimension.
        tasks.MapGet("/{id}/orphan-repair", (string id) => Results.NoContent());

        return tasks;
    }

    private static TaskResponse ToResponse(
        TaskItem task,
        IStoreView view,
        DimensionRegistry registry,
        StaleThresholds staleThresholds,
        DateTimeOffset now,
        DayBoundary boundary,
        OpportunityCounter opportunities)
    {
        var log = view.CompletionsFor(task.Id);
        var status = StatusRules.Of(task, log, registry, staleThresholds, now, boundary);
        var patternWeekCount = status is not Status.Active
            ? (int?)null
            : opportunities.CountInPatternWeek(task, view.Patterns.Active, view.DayTemplates, boundary.DateOf(now));
        var eligible = status is Status.Active
            && StatusRules.IsEligible(task, log, registry, staleThresholds, now, boundary);
        var opportunityCount = eligible
            ? opportunities.CountAhead(task, now, EmptyFetchedValues, FailedFetchedDimensions(task, registry))
            : null;
        var zeroKind = status is not Status.Active || patternWeekCount is not { } weekCount
            ? null
            : eligible
                ? OrphanDetection.KindOfZero(status, opportunityCount, weekCount)
                : OrphanDetection.IsTaskOrphan(status, weekCount) ? ZeroKind.Orphan : null;

        return new TaskResponse(
            task.Id.Value,
            task.Title,
            task.Notes,
            DurationOf(task),
            task.Tags.Dimensions.ToDictionary(
                dimension => dimension.Key.Value,
                dimension => (IReadOnlyList<string>)[.. dimension.Value.Select(value => value.Value)]),
            [.. task.Tags.LooseTags.Select(tag => tag.Value)],
            task.CreatedAt,
            ToWireName(status),
            eligible,
            DeadlineOf(task, log, now, boundary),
            DeferOf(task, log, now, boundary),
            task.Postpone,
            task.Recurrence is not null,
            task.Provenance is not null,
            opportunityCount,
            patternWeekCount,
            zeroKind is { } kind ? ToWireName(kind) : null);
    }

    private static readonly IReadOnlyDictionary<DimensionId, IReadOnlyList<TagValue>> EmptyFetchedValues =
        new Dictionary<DimensionId, IReadOnlyList<TagValue>>();

    private static IReadOnlyList<DimensionId> FailedFetchedDimensions(TaskItem task, DimensionRegistry registry) =>
        registry.Dimensions
            .Where(dimension => task.Tags.On(dimension.Id).Count > 0
                && dimension.Match(
                    categorical => categorical.WindowSource is WindowValueSource.Fetched,
                    ordinal => ordinal.WindowSource is WindowValueSource.Fetched))
            .Select(dimension => dimension.Id)
            .ToArray();

    private static string ToWireName<TEnum>(TEnum value) where TEnum : struct, Enum =>
        JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    /// <summary>The one Dimension the walking skeleton (#51) reads back out — the ordinal single value, if any.</summary>
    private static string? DurationOf(TaskItem task) =>
        task.Tags.SingleOn(KnownDimensions.Duration)?.Value;

    private static DateOnly? DeadlineOf(
        TaskItem task,
        CompletionLog log,
        DateTimeOffset now,
        DayBoundary boundary) =>
        task.Recurrence is { } recurrence
            ? RecurrenceRules.LiveInstanceDeadline(recurrence, task.CreatedAt, log, now, boundary)
            : task.Deadline;

    private static DateOnly? DeferOf(
        TaskItem task,
        CompletionLog log,
        DateTimeOffset now,
        DayBoundary boundary)
    {
        if (task.Defer is not { } defer)
        {
            return null;
        }

        var deadline = DeadlineOf(task, log, now, boundary);
        return defer.Match<DateOnly?>(
            absolute => task.Recurrence is null
                ? absolute.Date
                : throw new InvalidOperationException(
                    $"Task '{task.Id.Value}' is recurring but holds an absolute Defer of "
                    + $"{absolute.Date:yyyy-MM-dd}. A recurring Task must express its Defer as an "
                    + "Offset: an absolute date would apply to one instance and be wrong forever "
                    + "after. The write paths refuse this, so the stored record is corrupt."),
            offset => deadline is { } anchor
                ? OffsetRules.ResolveAgainst(offset.Offset, anchor)
                : null);
    }

    private static bool IsTaskId(string id) =>
        IsMintedTaskId(id) || IsDerivedTaskId(id);

    private static bool IsMintedTaskId(string id) =>
        id.Length == 28 && id.StartsWith(TaskId.Prefix, StringComparison.Ordinal) && id[2..].All(character => "0123456789ABCDEFGHJKMNPQRSTVWXYZ".Contains(character));

    private static bool IsDerivedTaskId(string id)
    {
        const string prefix = "t_derived_";
        if (!id.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var separator = id.IndexOf('_', prefix.Length);
        return separator > prefix.Length && separator < id.Length - 1;
    }

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

public sealed record TaskResponse(
    string Id,
    string Title,
    string? Notes,
    string? Duration,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Dimensions,
    IReadOnlyList<string> LooseTags,
    DateTimeOffset CreatedAt,
    string Status,
    bool Eligible,
    DateOnly? Deadline,
    DateOnly? Defer,
    DateOnly? Postpone,
    bool Recurring,
    bool Derived,
    int? Opportunities,
    int? PatternWeekCount,
    string? ZeroKind);

public sealed record PostponeTaskRequest(DateOnly Date);

public sealed record DeferTaskRequest(DateOnly? Date, int? Offset, OffsetUnit? Unit);

/// <summary>The canonical Duration bucket to author on an existing Task.</summary>
public sealed record SetTaskDurationRequest(string? Duration);

/// <summary>A logging-category marker — <see cref="TaskEndpoints"/> is static and can't be used as one directly.</summary>
public sealed class TaskEndpointsLogCategory;
