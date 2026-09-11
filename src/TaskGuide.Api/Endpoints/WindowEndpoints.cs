using Microsoft.AspNetCore.Http.HttpResults;
using TaskGuide.Application.Ports;
using TaskGuide.Application.Schedule;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
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
