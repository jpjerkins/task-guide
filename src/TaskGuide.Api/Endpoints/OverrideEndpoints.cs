using Microsoft.AspNetCore.Http.HttpResults;
using TaskGuide.Application.Ports;
using TaskGuide.Application.Schedule;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Time;

namespace TaskGuide.Api.Endpoints;

/// <summary>
/// Overrides — dated realities layered on the computed shape. <b>Everything under /overrides
/// edits one date</b>, the opposite verb from /day-templates and /patterns, which is why it is a
/// separate group rather than folded into the shape-editing surface.
/// </summary>
public static class OverrideEndpoints
{
    public static RouteGroupBuilder MapOverrideEndpoints(this RouteGroupBuilder api)
    {
        var overrides = api.MapGroup("/overrides").WithTags("Schedule");

        // Sparse and read by RANGE — the 3-day runway, the 7-day horizon.
        overrides.MapGet("/", () => Results.NoContent());
        overrides.MapGet("/{date}", (string date) => Results.NoContent());

        // ONE AUTHORING GESTURE, a start–end range, writing one Override per date — each
        // independently editable afterwards (#41). A range landing on already-overridden dates is
        // a replacement, confirmed in one batch before the write.
        overrides.MapPost("/", () => Results.NoContent());
        overrides.MapGet("/clobber-check", () => Results.NoContent());

        // Applying a named template is a STAMP, not a link, and the copy preserves each Window's
        // id — load-bearing for the Fire record when a date materialises mid-day.
        overrides.MapPut("/{date}/stamp", DayTemplateLifecycleHandlers.StampAsync);

        // Editing a stamped date directly makes it a one-off day; the use record survives that.
        overrides.MapPatch("/{date}", (string date) => Results.NoContent());
        overrides.MapDelete("/{date}", (string date) => Results.NoContent());

        // Promotion copies the shape OUTWARD; the source date keeps its own copy and does not
        // re-link, but it DOES get a use record — otherwise "keep this" produces something born
        // `Unused`, and the Christmas Day case sits deletable for eleven months.
        overrides.MapPost("/{date}/promote", DayTemplateLifecycleHandlers.PromoteAsync);

        return api;
    }
}

internal static class DayTemplateLifecycleHandlers
{
    public static async Task<Results<Ok<DayTemplateResponse>, BadRequest<object>, Conflict<object>>> PromoteAsync(
        string date, PromoteDayRequest request, IStore store, IIdMinter minter, CancellationToken ct)
    {
        if (!DateOnly.TryParse(date, out var sourceDate) || string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.BadRequest<object>(new { error = "a date and template name are required" });
        }

        var template = new DayTemplate(minter.NextDayTemplateId(), request.Name, [], []);
        var result = await new PromoteOneOffDay(store).ExecuteAsync(sourceDate, template, ct);
        return result.Match<Results<Ok<DayTemplateResponse>, BadRequest<object>, Conflict<object>>>(
            promoted => TypedResults.Ok(ToResponse(promoted)),
            refusal => TypedResults.Conflict<object>(new { error = refusal.Reason }));
    }

    public static async Task<Results<Ok<DateOverrideResponse>, BadRequest<object>, Conflict<object>>> StampAsync(
        string date, StampDayRequest request, IStore store, CancellationToken ct)
    {
        if (!DateOnly.TryParse(date, out var stampedDate) || !IsDayTemplateId(request.TemplateId))
        {
            return TypedResults.BadRequest<object>(new { error = "a date and Day template id are required" });
        }

        var result = await new StampDayTemplate(store).ExecuteAsync(stampedDate, new DayTemplateId(request.TemplateId), ct);
        return result.Match<Results<Ok<DateOverrideResponse>, BadRequest<object>, Conflict<object>>>(
            stamped => TypedResults.Ok(ToResponse(stamped)),
            refusal => TypedResults.Conflict<object>(new { error = refusal.Reason }));
    }

    public static async Task<Results<NoContent, BadRequest<object>, Conflict<object>>> DeleteAsync(
        string id, IStore store, TimeProvider clock, DayBoundary boundary, CancellationToken ct)
    {
        if (!IsDayTemplateId(id)) return TypedResults.BadRequest<object>(new { error = "id must be a Day template id" });

        var result = await new DeleteDayTemplate(store, clock, boundary).ExecuteAsync(new DayTemplateId(id), ct);
        return result.Match<Results<NoContent, BadRequest<object>, Conflict<object>>>(
            _ => TypedResults.NoContent(),
            refusal => TypedResults.Conflict<object>(new { error = refusal.Reason }));
    }

    public static Results<Ok<IEnumerable<string>>, BadRequest<object>> Usage(string id, IStore store)
    {
        if (!IsDayTemplateId(id)) return TypedResults.BadRequest<object>(new { error = "id must be a Day template id" });

        var templateId = new DayTemplateId(id);
        return TypedResults.Ok(store.Read().Patterns.Patterns
            .Where(pattern => pattern.Days.Contains(templateId))
            .Select(pattern => pattern.Name));
    }

    private static bool IsDayTemplateId(string? value) =>
        value is { Length: 29 }
        && value.StartsWith(DayTemplateId.Prefix, StringComparison.Ordinal)
        && value[DayTemplateId.Prefix.Length..].All(character => "0123456789ABCDEFGHJKMNPQRSTVWXYZ".Contains(character));

    private static DayTemplateResponse ToResponse(DayTemplate template) =>
        new(template.Id.Value, template.Name, template.Windows, template.EventPrototypes);

    private static DateOverrideResponse ToResponse(DateOverride dateOverride) =>
        new(
            dateOverride.Date,
            dateOverride.Windows,
            dateOverride.Used is null ? null : new DayTemplateUseResponse(dateOverride.Used.TemplateId.Value, dateOverride.Used.TemplateName));
}

public sealed record PromoteDayRequest(string Name);
public sealed record StampDayRequest(string TemplateId);
public sealed record DayTemplateResponse(string Id, string Name, IReadOnlyList<AvailabilityWindow> Windows, IReadOnlyList<EventPrototype> EventPrototypes);
public sealed record DateOverrideResponse(DateOnly Date, IReadOnlyList<AvailabilityWindow> Windows, DayTemplateUseResponse? Used);
public sealed record DayTemplateUseResponse(string TemplateId, string TemplateName);
