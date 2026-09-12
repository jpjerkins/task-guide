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
        overrides.MapPost("/", CreateAsync);
        overrides.MapGet("/clobber-check", ClobberCheck);

        // Applying a named template is a STAMP, not a link, and the copy preserves each Window's
        // id — load-bearing for the Fire record when a date materialises mid-day.
        overrides.MapPut("/{date}/stamp", DayTemplateLifecycleHandlers.StampAsync);

        // Editing a stamped date directly makes it a one-off day; the use record survives that.
        overrides.MapPatch("/{date}", EditAsync);
        overrides.MapDelete("/{date}", DeleteAsync);

        // Promotion copies the shape OUTWARD; the source date keeps its own copy and does not
        // re-link, but it DOES get a use record — otherwise "keep this" produces something born
        // `Unused`, and the Christmas Day case sits deletable for eleven months.
        overrides.MapPost("/{date}/promote", DayTemplateLifecycleHandlers.PromoteAsync);

        return api;
    }

    private static async Task<Results<Created<DateOverrideResponse[]>, BadRequest<object>, Conflict<object>>> CreateAsync(
        OverrideSpanApiRequest request, IStore store, CancellationToken ct)
    {
        if (!DateOnly.TryParse(request.From, out var from) || !DateOnly.TryParse(request.To, out var to) || to < from ||
            (request.TemplateId is not null && !DayTemplateLifecycleHandlers.IsDayTemplateId(request.TemplateId)))
        {
            return TypedResults.BadRequest<object>(new { error = "valid start and end dates are required" });
        }

        DayTemplateId? templateId = request.TemplateId is null ? null : new DayTemplateId(request.TemplateId);
        var outcome = await new CreateOverrideSpan(store).ExecuteAsync(new OverrideSpanRequest(from, to, templateId), ct);
        return outcome.Match<Results<Created<DateOverrideResponse[]>, BadRequest<object>, Conflict<object>>>(
            _ => TypedResults.Created("/api/overrides", store.Read().Overrides
                .Where(overrideDay => overrideDay.Date >= from && overrideDay.Date <= to)
                .OrderBy(overrideDay => overrideDay.Date)
                .Select(DayTemplateLifecycleHandlers.ToResponse)
                .ToArray()),
            refusal => TypedResults.Conflict<object>(new { error = refusal.Reason }));
    }

    private static Results<Ok<IReadOnlyList<DateOnly>>, BadRequest<object>> ClobberCheck(string? from, string? to, IStore store)
    {
        if (!DateOnly.TryParse(from, out var start) || !DateOnly.TryParse(to, out var end) || end < start)
        {
            return TypedResults.BadRequest<object>(new { error = "valid start and end dates are required" });
        }

        return TypedResults.Ok<IReadOnlyList<DateOnly>>([.. store.Read().Overrides
            .Where(overrideDay => overrideDay.Date >= start && overrideDay.Date <= end)
            .Select(overrideDay => overrideDay.Date)
            .OrderBy(date => date)]);
    }

    private static async Task<Results<Ok<DateOverrideResponse>, BadRequest<object>, Conflict<object>>> EditAsync(
        string date, EditOverrideRequest request, IStore store, CancellationToken ct)
    {
        if (!DateOnly.TryParse(date, out var overrideDate) || request.Windows is null)
        {
            return TypedResults.BadRequest<object>(new { error = "a date and Windows are required" });
        }

        var outcome = await new EditOverride(store).ExecuteAsync(overrideDate, request.Windows, ct);
        return outcome.Match<Results<Ok<DateOverrideResponse>, BadRequest<object>, Conflict<object>>>(
            _ => TypedResults.Ok(DayTemplateLifecycleHandlers.ToResponse(store.Read().Overrides.Single(overrideDay => overrideDay.Date == overrideDate))),
            refusal => TypedResults.Conflict<object>(new { error = refusal.Reason }));
    }

    private static async Task<Results<NoContent, BadRequest<object>, Conflict<object>>> DeleteAsync(
        string date, IStore store, CancellationToken ct)
    {
        if (!DateOnly.TryParse(date, out var overrideDate))
        {
            return TypedResults.BadRequest<object>(new { error = "a date is required" });
        }

        var outcome = await new DeleteOverride(store).ExecuteAsync(overrideDate, ct);
        return outcome.Match<Results<NoContent, BadRequest<object>, Conflict<object>>>(
            _ => TypedResults.NoContent(),
            refusal => TypedResults.Conflict<object>(new { error = refusal.Reason }));
    }
}

internal static class DayTemplateLifecycleHandlers
{
    public static async Task<Results<Ok<DayTemplateResponse>, BadRequest<object>, Conflict<object>>> PromoteAsync(
        string date, PromoteDayRequest request, IStore store, IIdMinter minter, TimeProvider clock, DayBoundary boundary, CancellationToken ct)
    {
        if (!DateOnly.TryParse(date, out var sourceDate) || string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.BadRequest<object>(new { error = "a date and template name are required" });
        }

        var template = new DayTemplate(minter.NextDayTemplateId(), request.Name, [], []);
        var result = await new PromoteOneOffDay(store).ExecuteAsync(sourceDate, template, ct);
        return result.Match<Results<Ok<DayTemplateResponse>, BadRequest<object>, Conflict<object>>>(
            promoted =>
            {
                var view = store.Read();
                return TypedResults.Ok(ToResponse(promoted, view.Patterns.Patterns, view.Overrides, boundary.DateOf(clock.GetUtcNow())));
            },
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

    internal static bool IsDayTemplateId(string? value) =>
        value is { Length: 29 }
        && value.StartsWith(DayTemplateId.Prefix, StringComparison.Ordinal)
        && value[DayTemplateId.Prefix.Length..].All(character => "0123456789ABCDEFGHJKMNPQRSTVWXYZ".Contains(character));

    internal static DayTemplateResponse ToResponse(
        DayTemplate template,
        IReadOnlyList<Pattern> allPatterns,
        IReadOnlyList<DateOverride> overrides,
        DateOnly today) =>
        new(
            template.Id.Value,
            template.Name,
            template.Windows,
            template.EventPrototypes,
            DayTemplateLifecycle.IsUnused(template.Id, allPatterns, overrides, today));

    internal static DateOverrideResponse ToResponse(DateOverride dateOverride) =>
        new(
            dateOverride.Date,
            dateOverride.Windows,
            dateOverride.Used is null ? null : new DayTemplateUseResponse(dateOverride.Used.TemplateId.Value, dateOverride.Used.TemplateName));
}

public sealed record PromoteDayRequest(string Name);
public sealed record StampDayRequest(string TemplateId);
public sealed record OverrideSpanApiRequest(string From, string To, string? TemplateId);
public sealed record EditOverrideRequest(IReadOnlyList<AvailabilityWindow>? Windows);
public sealed record DayTemplateResponse(
    string Id,
    string Name,
    IReadOnlyList<AvailabilityWindow> Windows,
    IReadOnlyList<EventPrototype> EventPrototypes,
    bool Unused);
public sealed record DateOverrideResponse(DateOnly Date, IReadOnlyList<AvailabilityWindow> Windows, DayTemplateUseResponse? Used);
public sealed record DayTemplateUseResponse(string TemplateId, string TemplateName);
