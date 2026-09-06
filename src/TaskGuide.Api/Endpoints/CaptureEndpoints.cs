using Microsoft.AspNetCore.Http.HttpResults;
using TaskGuide.Application.Capture;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Notifications;
using TaskGuide.Domain.Tasks;

namespace TaskGuide.Api.Endpoints;

/// <summary>
/// All three iOS Shortcuts write the <b>same</b> Task through this one endpoint, which carries a
/// <b>source</b>. The Receipt policy reads that source — keeping the decision in one place rather
/// than letting each capture path decide.
/// </summary>
/// <remarks>
/// Duration is the only property capture must supply; a capture that drops it produces an
/// `Unprocessed` Task, which <em>is</em> that label's definition. No capture path collects Tags —
/// they are added afterwards through the Receipt. <b>Capture is never queued</b>: one attempt
/// against a reachable server, failing loudly at the moment of capture.
/// </remarks>
public static class CaptureEndpoints
{
    private static int? DurationOf(TaskItem task) =>
        task.Tags.SingleOn(KnownDimensions.Duration) is { } duration ? int.Parse(duration.Value) : null;

    public static RouteGroupBuilder MapCaptureEndpoints(this RouteGroupBuilder api)
    {
        var capture = api.MapGroup("/capture").WithTags("Capture");

        // { title, duration?, source }. The server snaps raw minutes UP to a bucket, so a capture
        // path may send "45". Source remains open: only the app's own value suppresses a Receipt.
        capture.MapPost("/", async Task<Results<Created<CaptureTaskResponse>, BadRequest<object>, ProblemHttpResult>> (
            CaptureRequest request,
            HttpRequest httpRequest,
            IStore store,
            IIdMinter minter,
            IReceiptSender receipts,
            TimeProvider timeProvider,
            ILogger<CaptureEndpointsLogCategory> logger,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return TypedResults.BadRequest<object>(new { error = "title is required" });
            }

            if (request.Duration is <= 0)
            {
                return TypedResults.BadRequest<object>(new { error = "duration must be a positive integer" });
            }

            if (string.IsNullOrWhiteSpace(request.Source))
            {
                return TypedResults.BadRequest<object>(new { error = "source is required" });
            }

            try
            {
                var command = new CaptureTask(store, minter, receipts, timeProvider);
                var task = await command.ExecuteAsync(
                    new CaptureTaskRequest(request.Title, request.Duration, request.Source),
                    id => new Uri($"{httpRequest.Scheme}://{httpRequest.Host}/api/tasks/{id.Value}"),
                    ct);
                var response = new CaptureTaskResponse(task.Id.Value, task.Title, request.Duration is null ? null : DurationOf(task), task.CreatedAt);
                return TypedResults.Created($"/api/tasks/{task.Id.Value}", response);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to capture Task");
                return TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Storage is temporarily unavailable");
            }
        })
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return capture;
    }
}

public sealed record CaptureRequest(string Title, int? Duration, string Source);
public sealed record CaptureTaskResponse(string Id, string Title, int? Duration, DateTimeOffset CreatedAt);
public sealed class CaptureEndpointsLogCategory;
