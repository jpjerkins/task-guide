using Microsoft.AspNetCore.Http.HttpResults;
using TaskGuide.Application.Firing;
using TaskGuide.Application.Ports;
using TaskGuide.Application.Reminders;
using TaskGuide.Application.Rules;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Notifications;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.Domain.Time;

namespace TaskGuide.Api.Endpoints;

/// <summary>
/// The landing page a notification opens — the highest-traffic screen in the system, because
/// Pushover carries one URL and nothing actionable, so snooze and inline triage have nowhere
/// else to live.
/// </summary>
public static class ReminderEndpoints
{
    public static RouteGroupBuilder MapReminderEndpoints(this RouteGroupBuilder api)
    {
        var reminders = api.MapGroup("/reminders").WithTags("Reminders");

        // Keyed like the Fire record: (date, windowId, kind). Shows ALL matches, not the push's
        // three. The response carries the page-level gate — is this page still about a live day —
        // which is what disables Snooze and "Matching on" while Mark off and Postpone stay live.
        reminders.MapGet("/{date}/{windowId}", async Task<Results<Ok<ReminderPageResponse>, NotFound, BadRequest<object>>> (
            string date, string windowId,
            IStoreReader store, IDayShapeReader shapes, DimensionRegistry registry, ClockTimeResolution resolution,
            DayBoundary boundary, StaleThresholds staleThresholds, DerivedTaskComposer derivedTasks, TickPlanner planner,
            IWeatherSource weather, TimeProvider timeProvider, CancellationToken ct) =>
        {
            if (!DateOnly.TryParse(date, out var reminderDate))
                return TypedResults.BadRequest<object>(new { error = "date must be an ISO date" });

            var outcome = await new ReadReminderPage(
                store, shapes, registry, resolution, boundary, staleThresholds, derivedTasks, planner, weather, timeProvider)
                .ExecuteAsync(reminderDate, windowId, ct);

            return outcome.Match<Results<Ok<ReminderPageResponse>, NotFound, BadRequest<object>>>(
                page => TypedResults.Ok(ToResponse(page)),
                _ => TypedResults.NotFound());
        });

        // The predicate is server-side and the UI reads it. A crossing request is REJECTED, and
        // the rejection renders as the same line the disabled state would have shown.
        reminders.MapPost("/{date}/{windowId}/snooze", async Task<IResult> (string date, string windowId, IStore store, TimeProvider timeProvider, DayBoundary boundary, CancellationToken ct) =>
        {
            if (!DateOnly.TryParse(date, out var reminderDate))
                return TypedResults.BadRequest(new { error = "date must be an ISO date" });
            var outcome = await new SnoozeWindow(store, timeProvider, boundary).ExecuteAsync(reminderDate, new WindowId(windowId), ct);
            return outcome.Match<IResult>(
                _ => TypedResults.NoContent(),
                unavailable => TypedResults.Conflict(new { error = unavailable.Line }),
                _ => TypedResults.NotFound());
        });

        // The audit trail for "why did I not get a reminder?" — 30-day retention.
        reminders.MapGet("/fires/{date}", (string date) => Results.NoContent());

        return reminders;
    }

    private static ReminderPageResponse ToResponse(ReminderPage page)
    {
        var (windowName, windowStart, windowEnd, snooze, fallbackEventName) = page.Context.Match(
            window => (
                (string?)window.Name, (TimeOnly?)window.Start, (TimeOnly?)window.End,
                (SnoozeOffer?)new SnoozeOffer(window.SnoozeIntervalMinutes, window.SnoozeSuppression), (string?)null),
            fallback => ((string?)null, (TimeOnly?)null, (TimeOnly?)null, (SnoozeOffer?)null, (string?)fallback.EventName));

        return new ReminderPageResponse(
            page.Date,
            windowName, windowStart, windowEnd,
            snooze,
            fallbackEventName,
            page.FiredAs is { } kind ? FiredAsOf(kind) : null,
            [.. page.Matches.Select(ToTaskResponse)],
            page.IsLive,
            page.StaleLine,
            new MatchingOnResponse(ToAxes(page.MatchingOn.Declared), ToAxes(page.MatchingOn.Defaulted)),
            new FooterCountsResponse(page.Footer.ToProcess, page.Footer.Stale, page.Footer.Orphans),
            [.. page.FailedFetches.Select(id => id.Value)]);
    }

    private static string FiredAsOf(FireKind kind) => kind switch
    {
        FireKind.Window => "window",
        FireKind.Unconditional => "unconditional",
        FireKind.Snooze => "snooze",
        FireKind.Fallback => "fallback",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown FireKind."),
    };

    private static IReadOnlyDictionary<string, string[]> ToAxes(IReadOnlyDictionary<DimensionId, IReadOnlyList<TagValue>> axes) =>
        axes.ToDictionary(pair => pair.Key.Value, pair => pair.Value.Select(value => value.Value).ToArray());

    private static TaskResponse ToTaskResponse(TaskItem task) => new(
        task.Id.Value,
        task.Title,
        task.Tags.SingleOn(KnownDimensions.Duration) is { } duration ? int.Parse(duration.Value) : null,
        task.CreatedAt);
}

public sealed record ReminderPageResponse(
    DateOnly Date,
    string? WindowName, TimeOnly? WindowStart, TimeOnly? WindowEnd,
    SnoozeOffer? Snooze,
    string? FallbackEventName,
    string? FiredAs,
    IReadOnlyList<TaskResponse> Matches,
    bool IsLive,
    string? StaleLine,
    MatchingOnResponse MatchingOn,
    FooterCountsResponse Footer,
    IReadOnlyList<string> FailedFetches);

public sealed record SnoozeOffer(int IntervalMinutes, string? Suppression);

public sealed record MatchingOnResponse(
    IReadOnlyDictionary<string, string[]> Declared,
    IReadOnlyDictionary<string, string[]> Defaulted);

public sealed record FooterCountsResponse(int ToProcess, int Stale, int Orphans);
