using Microsoft.AspNetCore.Http.HttpResults;
using TaskGuide.Application.Ports;
using TaskGuide.Application.Rules;
using TaskGuide.Application.Schedule;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Matching;
using TaskGuide.Domain.Ranking;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.Domain.Time;

namespace TaskGuide.Api.Endpoints;

/// <summary>
/// Windows within a Day template. Per-day instances, not shared definitions, so editing one never
/// propagates — the opposite rule from the template itself.
/// </summary>
public static class WindowEndpoints
{
    public static RouteGroupBuilder MapWindowEndpoints(this RouteGroupBuilder api)
    {
        var templates = api.MapGroup("/day-templates").WithTags("Schedule");

        // Windows are per-day instances, not shared definitions; editing one never propagates.
        templates.MapPost("/{id}/windows", CreateAsync);
        templates.MapPatch("/{id}/windows/{windowId}", EditAsync);
        templates.MapDelete("/{id}/windows/{windowId}", DeleteAsync);

        // The inverse warning, shown before a Dimension value is removed: "N Tasks depend on this
        // and nothing else declares it" — catching Drift at the edit that causes it.
        templates.MapGet("/{id}/windows/{windowId}/dependents", Dependents);

        // "What would surface here": a structural preview of a Window's match — the inverse of
        // the Dependents warning above, which is about removing a Dimension value.
        templates.MapGet("/{id}/windows/{windowId}/preview", Preview);

        return api;
    }

    private static async Task<Results<Created<AvailabilityWindow>, BadRequest<object>, Conflict<object>>> CreateAsync(
        string id, WindowRequest request, IStore store, IIdMinter minter, CancellationToken ct)
    {
        if (!DayTemplateLifecycleHandlers.IsDayTemplateId(id) || !IsValid(request))
        {
            return TypedResults.BadRequest<object>(new { error = "a Day template id, name, increasing times, and Tags are required" });
        }

        var window = new AvailabilityWindow(minter.NextWindowId(), request.Name, request.Start, request.End, request.Tags);
        var outcome = await new CreateWindow(store).ExecuteAsync(new DayTemplateId(id), window, ct);
        return outcome.Match<Results<Created<AvailabilityWindow>, BadRequest<object>, Conflict<object>>>(
            created => TypedResults.Created($"/api/day-templates/{id}/windows/{created.Window.Id.Value}", created.Window),
            _ => TypedResults.Conflict<object>(new { error = "Day template was not found" }));
    }

    private static async Task<Results<Ok<AvailabilityWindow>, BadRequest<object>, Conflict<object>>> EditAsync(
        string id, string windowId, WindowRequest request, IStore store, CancellationToken ct)
    {
        if (!DayTemplateLifecycleHandlers.IsDayTemplateId(id) || !IsWindowId(windowId) || !IsValid(request))
        {
            return TypedResults.BadRequest<object>(new { error = "a Day template id, Window id, name, increasing times, and Tags are required" });
        }

        var window = new AvailabilityWindow(new WindowId(windowId), request.Name, request.Start, request.End, request.Tags);
        var outcome = await new EditWindow(store).ExecuteAsync(new DayTemplateId(id), window, ct);
        return outcome.Match<Results<Ok<AvailabilityWindow>, BadRequest<object>, Conflict<object>>>(
            edited => TypedResults.Ok(edited.Window),
            _ => TypedResults.Conflict<object>(new { error = "Day template or Window was not found" }));
    }

    private static async Task<Results<NoContent, BadRequest<object>, Conflict<object>>> DeleteAsync(
        string id, string windowId, IStore store, CancellationToken ct)
    {
        if (!DayTemplateLifecycleHandlers.IsDayTemplateId(id) || !IsWindowId(windowId))
        {
            return TypedResults.BadRequest<object>(new { error = "a Day template id and Window id are required" });
        }

        var outcome = await new DeleteWindow(store).ExecuteAsync(new DayTemplateId(id), new WindowId(windowId), ct);
        return outcome.Match<Results<NoContent, BadRequest<object>, Conflict<object>>>(
            _ => TypedResults.NoContent(),
            _ => TypedResults.Conflict<object>(new { error = "Day template or Window was not found" }));
    }

    private static Results<Ok<IReadOnlyList<WindowValueDependentsResponse>>, BadRequest<object>> Dependents(
        string id,
        string windowId,
        IStore store,
        IDayShapeReader shapes,
        DimensionRegistry registry,
        ClockTimeResolution resolution,
        DayBoundary boundary,
        StaleThresholds staleThresholds,
        TimeProvider clock)
    {
        if (!DayTemplateLifecycleHandlers.IsDayTemplateId(id) || !IsWindowId(windowId))
        {
            return TypedResults.BadRequest<object>(new { error = "a Day template id and Window id are required" });
        }

        var view = store.Read();
        var templateId = new DayTemplateId(id);
        var idOfWindow = new WindowId(windowId);
        if (!view.DayTemplates.Any(template => template.Id.Equals(templateId) && template.Windows.Any(window => window.Id.Equals(idOfWindow))))
        {
            return TypedResults.BadRequest<object>(new { error = "a known Day template and Window are required" });
        }

        var dependents = Drift.DependentsForRemovingEachValue(
            view.Tasks,
            view.CompletionsFor,
            view.Patterns.Active,
            view.DayTemplates,
            templateId,
            idOfWindow,
            new OpportunityCounter(shapes, registry, resolution, boundary),
            registry,
            staleThresholds,
            clock.GetUtcNow(),
            boundary);

        return TypedResults.Ok<IReadOnlyList<WindowValueDependentsResponse>>(
            dependents.Select(dependent => new WindowValueDependentsResponse(
                dependent.DimensionId.Value,
                dependent.Value.Value,
                dependent.DependentTasks)).ToArray());
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
        if (!DayTemplateLifecycleHandlers.IsDayTemplateId(id) || !IsWindowId(windowId) || !DateOnly.TryParse(date, out var onDate))
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

    private static bool IsValid(WindowRequest request) =>
        !string.IsNullOrWhiteSpace(request.Name)
        && request.End > request.Start;

    internal static bool IsWindowId(string? value) =>
        value is { Length: 28 }
        && value.StartsWith(WindowId.Prefix, StringComparison.Ordinal)
        && value[WindowId.Prefix.Length..].All(character => "0123456789ABCDEFGHJKMNPQRSTVWXYZ".Contains(character));
}

public sealed record WindowRequest(string Name, TimeOnly Start, TimeOnly End, TagSet Tags);
public sealed record WindowValueDependentsResponse(string DimensionId, string Value, int DependentTasks);
public sealed record WindowMatchPreviewResponse(int Count, IReadOnlyList<string> Titles);
