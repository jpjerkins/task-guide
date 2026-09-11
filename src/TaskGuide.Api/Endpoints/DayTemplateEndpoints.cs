using Microsoft.AspNetCore.Http.HttpResults;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Time;

namespace TaskGuide.Api.Endpoints;

/// <summary>
/// Day templates — reusable shapes. <b>Everything under /day-templates edits a shape, so it
/// propagates</b> to every date the shape is stamped onto, which is why usage and dependents are
/// surfaced before a destructive edit rather than after.
/// </summary>
public static class DayTemplateEndpoints
{
    public static RouteGroupBuilder MapDayTemplateEndpoints(this RouteGroupBuilder api)
    {
        var templates = api.MapGroup("/day-templates").WithTags("Schedule");
        templates.MapGet("/", List);
        templates.MapGet("/{id}", GetById);
        templates.MapPost("/", () => Results.NoContent());
        templates.MapPatch("/{id}", (string id) => Results.NoContent());

        // Gated on `Unused` — derived, never stored. Reachable from nothing, so the delete cannot
        // corrupt any record and the dangerous case is unrepresentable rather than warned about.
        // The confirmation names any Event prototypes carried, since those hold Absence notices.
        templates.MapDelete("/{id}", DayTemplateLifecycleHandlers.DeleteAsync);

        // Shown BEFORE saving an edit: "used by 3 Patterns: Volleyball, Summer, School year".
        // Blast radius is made visible, not prevented.
        templates.MapGet("/{id}/usage", DayTemplateLifecycleHandlers.Usage);

        // The scope banner's blast-radius count and list: the next fortnight's dates this
        // template actually governs, each flagged with whether it holds an Override. An Override
        // is a by-value copy of the Windows (`DayTemplate`'s own doc), so a Window edit never
        // reaches an overridden date — but the active Pattern's Event prototypes still layer onto
        // it (`DayShapeReader.For`), so an overridden date is reported, not omitted. Blast radius
        // is made visible, not prevented.
        templates.MapGet("/{id}/affected-dates", AffectedDates);

        templates.MapPost("/{id}/event-prototypes", (string id) => Results.NoContent());
        templates.MapPatch("/{id}/event-prototypes/{prototypeId}", (string id, string prototypeId) => Results.NoContent());
        templates.MapDelete("/{id}/event-prototypes/{prototypeId}", (string id, string prototypeId) => Results.NoContent());

        return api;
    }

    private static Ok<IEnumerable<DayTemplateResponse>> List(IStore store, TimeProvider clock, DayBoundary boundary)
    {
        var view = store.Read();
        var today = boundary.DateOf(clock.GetUtcNow());
        return TypedResults.Ok(view.DayTemplates.Select(template =>
            DayTemplateLifecycleHandlers.ToResponse(template, view.Patterns.Patterns, view.Overrides, today)));
    }

    private static Results<Ok<DayTemplateResponse>, BadRequest<object>, NotFound<object>> GetById(
        string id, IStore store, TimeProvider clock, DayBoundary boundary)
    {
        if (!DayTemplateLifecycleHandlers.IsDayTemplateId(id))
        {
            return TypedResults.BadRequest<object>(new { error = "id must be a Day template id" });
        }

        var view = store.Read();
        var templateId = new DayTemplateId(id);
        var template = view.DayTemplates.SingleOrDefault(t => t.Id.Equals(templateId));
        if (template is null)
        {
            return TypedResults.NotFound<object>(new { error = "Day template was not found" });
        }

        var today = boundary.DateOf(clock.GetUtcNow());
        return TypedResults.Ok(DayTemplateLifecycleHandlers.ToResponse(template, view.Patterns.Patterns, view.Overrides, today));
    }

    /// <summary>
    /// The 14 dates from today the active Pattern names this template for, each flagged with
    /// whether it holds an Override. No date is dropped: an Override shields its date from a
    /// Window edit (it keeps its own copied Windows) but not from an Event-prototype edit —
    /// <c>DayShapeReader.For</c> always layers the template's Event prototypes on top.
    /// </summary>
    private static Results<Ok<IReadOnlyList<AffectedDateResponse>>, BadRequest<object>> AffectedDates(
        string id, IStore store, TimeProvider clock, DayBoundary boundary)
    {
        if (!DayTemplateLifecycleHandlers.IsDayTemplateId(id))
        {
            return TypedResults.BadRequest<object>(new { error = "id must be a Day template id" });
        }

        var view = store.Read();
        var templateId = new DayTemplateId(id);
        var today = boundary.DateOf(clock.GetUtcNow());
        var overriddenDates = view.Overrides.Select(dateOverride => dateOverride.Date).ToHashSet();
        var active = view.Patterns.Active;

        IReadOnlyList<AffectedDateResponse> dates = Enumerable.Range(0, 14)
            .Select(today.AddDays)
            .Where(date => active[date.DayOfWeek] == templateId)
            .Select(date => new AffectedDateResponse(date, overriddenDates.Contains(date)))
            .ToArray();

        return TypedResults.Ok(dates);
    }
}

public sealed record AffectedDateResponse(DateOnly Date, bool Overridden);
