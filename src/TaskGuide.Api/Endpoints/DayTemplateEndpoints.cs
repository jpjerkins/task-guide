using Microsoft.AspNetCore.Http.HttpResults;
using TaskGuide.Application.Ports;
using TaskGuide.Application.Rules;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Matching;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
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

        // "What would surface here": a structural preview of a Window's match — the inverse of
        // WindowEndpoints' Dependents warning, which is about removing a Dimension value.
        templates.MapGet("/{id}/windows/{windowId}/preview", Preview);

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

    /// <summary>
    /// "What would surface here": the count and first four titles of the eligible Tasks this
    /// Window would admit on a date — derived obligations composed in, eligibility gated, then
    /// <see cref="Matcher.Fits"/>. Eligibility is gated at the instant the Window would start on
    /// the previewed date, not the wall clock, so previewing a future date answers "what matches
    /// there" rather than "now".
    /// <para>
    /// The Duration ceiling deliberately uses the Window's full resolved length, not `TickPlanner`'s
    /// remaining-time rule (<c>window.End - now</c>, <c>TickPlanner.cs</c>): this asks what the
    /// Window admits as authored, not what a fire at some instant would still have room for.
    /// </para>
    /// <b>Known residual limitation:</b> derived obligations from <see cref="DerivedTaskComposer"/>
    /// still anchor to its own injected clock, not the previewed date — re-anchoring that lives
    /// in Application/Rules/, outside this endpoint's ownership.
    /// </summary>
    private static Results<Ok<WindowMatchPreviewResponse>, BadRequest<object>> Preview(
        string id,
        string windowId,
        string? date,
        IStore store,
        DerivedTaskComposer derivedTasks,
        DimensionRegistry registry,
        ClockTimeResolution resolution,
        StaleThresholds staleThresholds,
        DayBoundary boundary)
    {
        if (!DayTemplateLifecycleHandlers.IsDayTemplateId(id) || !WindowEndpoints.IsWindowId(windowId) || !DateOnly.TryParse(date, out var onDate))
        {
            return TypedResults.BadRequest<object>(new { error = "a Day template id, Window id, and date are required" });
        }

        var view = store.Read();
        var templateId = new DayTemplateId(id);
        var idOfWindow = new WindowId(windowId);
        var template = view.DayTemplates.SingleOrDefault(t => t.Id.Equals(templateId));
        var window = template?.Windows.SingleOrDefault(w => w.Id.Equals(idOfWindow));
        if (window is null)
        {
            return TypedResults.BadRequest<object>(new { error = "a known Day template and Window are required" });
        }

        var previewInstant = resolution.Resolve(onDate, window.Start);
        var buckets = DurationBucketsOf(registry);
        var ceiling = buckets.Count > 0 ? window.DurationCeiling(onDate, resolution, buckets) : default;
        var context = new MatchContext(window, ceiling, EveryFetchedValueOf(registry), FailedFetches: []);

        var matched = derivedTasks.Compose(view)
            .Where(task => StatusRules.IsEligible(task, view.CompletionsFor(task.Id), registry, staleThresholds, previewInstant, boundary))
            .Where(task => Matcher.Fits(task, context, registry))
            .ToArray();

        return TypedResults.Ok(new WindowMatchPreviewResponse(
            matched.Length,
            matched.Select(task => task.Title).Take(4).ToArray()));
    }

    /// <summary>
    /// Every value a fetched axis declares, so that axis constrains nothing — this preview asks
    /// a structural question about a date that may have no forecast at all. Mirrors
    /// `OpportunityCounter.EveryFetchedValue` (Ranking/Opportunities.cs), which this endpoint
    /// does not own and so replicates rather than calls.
    /// </summary>
    private static IReadOnlyDictionary<DimensionId, IReadOnlyList<TagValue>> EveryFetchedValueOf(DimensionRegistry registry) =>
        registry.Dimensions
            .Select(dimension => dimension.Value)
            .OfType<CategoricalDimension>()
            .Where(dimension => dimension.WindowSource == WindowValueSource.Fetched)
            .ToDictionary(dimension => dimension.Id, dimension => dimension.DeclaredValues);

    /// <summary>
    /// The ordinal axis whose window-side value derives from the Window's length, read off the
    /// registry's algebra. Mirrors `OpportunityCounter.DurationBuckets` (Ranking/Opportunities.cs).
    /// </summary>
    private static IReadOnlyList<TagValue> DurationBucketsOf(DimensionRegistry registry) =>
        registry.Dimensions
            .Select(dimension => dimension.Value)
            .OfType<OrdinalDimension>()
            .SingleOrDefault(dimension => dimension.WindowSource == WindowValueSource.Derived)
            ?.OrderedValues ?? Array.Empty<TagValue>();
}

public sealed record WindowMatchPreviewResponse(int Count, IReadOnlyList<string> Titles);
public sealed record AffectedDateResponse(DateOnly Date, bool Overridden);
