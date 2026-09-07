using Microsoft.AspNetCore.Http.HttpResults;
using TaskGuide.Application.Ports;
using TaskGuide.Application.Schedule;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Ranking;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tasks;
using TaskGuide.Domain.Time;

namespace TaskGuide.Api.Endpoints;

/// <summary>
/// Patterns — the weekday-to-Day-template mapping, and the single active one.
/// </summary>
public static class PatternEndpoints
{
    public static RouteGroupBuilder MapPatternEndpoints(this RouteGroupBuilder api)
    {
        var patterns = api.MapGroup("/patterns").WithTags("Schedule");
        patterns.MapGet("/", (IStore store) => TypedResults.Ok(store.Read().Patterns.Patterns.Select(ToResponse)));
        patterns.MapPost("/", CreateAsync);
        patterns.MapPatch("/{id}", EditAsync);

        // Any Pattern that is not the active one, confirmed by name. The confirmation deliberately
        // does NOT report which Day templates the deletion strands.
        patterns.MapDelete("/{id}", DeleteAsync);

        // Switching can orphan a whole class of Tasks at once, so the count comes UP FRONT.
        patterns.MapGet("/active/switch-impact", SwitchImpact);
        patterns.MapPut("/active", SwitchAsync);

        return api;
    }

    private static async Task<Results<Created<PatternResponse>, BadRequest<object>>> CreateAsync(
        PatternRequest request, IStore store, IIdMinter minter, CancellationToken ct)
    {
        if (!IsValid(request)) return TypedResults.BadRequest<object>(new { error = "a name and seven Day template ids are required" });

        var pattern = new Pattern(minter.NextPatternId(), request.Name, request.Days.Select(id => new DayTemplateId(id)).ToArray());
        await new CreatePattern(store).ExecuteAsync(pattern, ct);
        return TypedResults.Created($"/api/patterns/{pattern.Id.Value}", ToResponse(pattern));
    }

    private static async Task<Results<Ok<PatternResponse>, BadRequest<object>, Conflict<object>>> EditAsync(
        string id, PatternRequest request, IStore store, CancellationToken ct)
    {
        if (!IsPatternId(id) || !IsValid(request)) return TypedResults.BadRequest<object>(new { error = "a Pattern id, name and seven Day template ids are required" });

        var pattern = new Pattern(new PatternId(id), request.Name, request.Days.Select(day => new DayTemplateId(day)).ToArray());
        var outcome = await new EditPattern(store).ExecuteAsync(pattern, ct);
        return outcome.Match<Results<Ok<PatternResponse>, BadRequest<object>, Conflict<object>>>(
            edited => TypedResults.Ok(ToResponse(edited.Pattern)),
            _ => TypedResults.Conflict<object>(new { error = "Pattern was not found" }));
    }

    private static async Task<Results<NoContent, BadRequest<object>, Conflict<object>>> DeleteAsync(
        string id, IStore store, CancellationToken ct)
    {
        if (!IsPatternId(id)) return TypedResults.BadRequest<object>(new { error = "id must be a Pattern id" });

        var outcome = await new DeletePattern(store).ExecuteAsync(new PatternId(id), ct);
        return outcome.Match<Results<NoContent, BadRequest<object>, Conflict<object>>>(
            _ => TypedResults.NoContent(),
            refusal => TypedResults.Conflict<object>(new { error = refusal.Reason }));
    }

    private static Results<Ok<SwitchImpactResponse>, BadRequest<object>> SwitchImpact(
        string? to,
        IStore store,
        IDayShapeReader shapes,
        DimensionRegistry registry,
        ClockTimeResolution resolution,
        DayBoundary boundary,
        StaleThresholds staleThresholds,
        TimeProvider clock)
    {
        if (to is not { } targetId || !IsPatternId(targetId)) return TypedResults.BadRequest<object>(new { error = "to must be a known Pattern id" });

        var view = store.Read();
        var candidate = view.Patterns.Patterns.SingleOrDefault(pattern => pattern.Id.Equals(new PatternId(targetId)));
        if (candidate is null) return TypedResults.BadRequest<object>(new { error = "to must be a known Pattern id" });

        var now = clock.GetUtcNow();
        var counter = new OpportunityCounter(shapes, registry, resolution, boundary);
        var newlyOrphaned = Drift.CountNewlyOrphaned(
            view.Tasks,
            view.CompletionsFor,
            view.Patterns.Active,
            candidate,
            view.DayTemplates,
            view.DayTemplates,
            counter,
            registry,
            staleThresholds,
            now,
            boundary);

        return TypedResults.Ok(new SwitchImpactResponse(newlyOrphaned));
    }

    private static async Task<Results<NoContent, BadRequest<object>, Conflict<object>>> SwitchAsync(
        SwitchActivePatternRequest request, IStore store, CancellationToken ct)
    {
        if (!IsPatternId(request.PatternId)) return TypedResults.BadRequest<object>(new { error = "a Pattern id is required" });

        var outcome = await new SwitchActivePattern(store).ExecuteAsync(new PatternId(request.PatternId), ct);
        return outcome.Match<Results<NoContent, BadRequest<object>, Conflict<object>>>(
            _ => TypedResults.NoContent(),
            _ => TypedResults.Conflict<object>(new { error = "Pattern was not found" }));
    }

    private static bool IsValid(PatternRequest request) =>
        !string.IsNullOrWhiteSpace(request.Name)
        && request.Days.Count == 7
        && request.Days.All(IsDayTemplateId);

    private static bool IsPatternId(string? value) => IsId(value, PatternId.Prefix, 28);

    private static bool IsDayTemplateId(string? value) => IsId(value, DayTemplateId.Prefix, 29);

    private static bool IsId(string? value, string prefix, int length) =>
        value is { Length: var valueLength } && valueLength == length
        && value.StartsWith(prefix, StringComparison.Ordinal)
        && value[prefix.Length..].All(character => "0123456789ABCDEFGHJKMNPQRSTVWXYZ".Contains(character));

    private static PatternResponse ToResponse(Pattern pattern) =>
        new(pattern.Id.Value, pattern.Name, pattern.Days.Select(day => day.Value).ToArray());
}

public sealed record PatternRequest(string Name, IReadOnlyList<string> Days);
public sealed record SwitchActivePatternRequest(string PatternId);
public sealed record PatternResponse(string Id, string Name, IReadOnlyList<string> Days);
public sealed record SwitchImpactResponse(int NewlyOrphaned);
