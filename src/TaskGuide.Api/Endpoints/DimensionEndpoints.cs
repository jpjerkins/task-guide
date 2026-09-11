using TaskGuide.Application.Ports;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Tags;

namespace TaskGuide.Api.Endpoints;

/// <summary>
/// <b>Read-only, and that is the whole point.</b> "No UI for managing Dimensions" stands —
/// adding one is a code change — but a wrong reminder is otherwise undebuggable.
/// </summary>
public static class DimensionEndpoints
{
    public static RouteGroupBuilder MapDimensionEndpoints(this RouteGroupBuilder api)
    {
        var dimensions = api.MapGroup("/dimensions").WithTags("Dimensions");

        // Identity, label, algebra, value set, and — ordinal only — the two defaults.
        dimensions.MapGet("/", (DimensionRegistry registry) =>
            TypedResults.Ok(registry.Dimensions.Select(ToResponse)));

        // Which Dimension will claim a string as it is typed, or plainly that nothing will. This
        // is the only moment the system can catch a mistyped Tag, which is otherwise invisible to
        // every mechanism in the model: `#garge` claims nothing, so it admits the Task to MORE
        // Windows than the Tag its author meant.
        dimensions.MapGet("/claiming", (string tag, DimensionRegistry registry) =>
            TypedResults.Ok(new ClaimingDimensionResponse(registry.Claiming(new TagValue(tag))?.Id.Value)));

        // The inert-tags staging area, with its count — tags resolving to no Dimension.
        dimensions.MapGet("/loose-tags", (IStore store) =>
        {
            var tags = store.Read().Tasks.SelectMany(task => task.Tags.LooseTags).Select(tag => tag.Value).ToArray();
            return TypedResults.Ok(new LooseTagsResponse(tags, tags.Length));
        });

        return dimensions;
    }

    private static DimensionResponse ToResponse(Dimension dimension) => dimension.Match(
        categorical => new DimensionResponse(
            categorical.Id.Value,
            categorical.Label,
            "categorical",
            categorical.DeclaredValues.Select(value => value.Value).ToArray(),
            TaskDefault: null,
            WindowDefault: null,
            Source: SourceOf(categorical.WindowSource)),
        ordinal => new DimensionResponse(
            ordinal.Id.Value,
            ordinal.Label,
            "ordinal",
            ordinal.OrderedValues.Select(value => value.Value).ToArray(),
            ordinal.TaskDefault?.Value,
            ordinal.WindowDefault?.Value,
            Source: SourceOf(ordinal.WindowSource)));

    // The discard arm is the enum idiom (an enum's underlying int admits values no case names, so
    // the compiler demands it), not a sign this is unreachable. The three strings are the wire
    // contract, deliberately not derived from the member names — renaming a member, or adding a
    // multi-word one, must not silently change or invent an API value.
    private static string SourceOf(WindowValueSource source) => source switch
    {
        WindowValueSource.Authored => "authored",
        WindowValueSource.Derived => "derived",
        WindowValueSource.Fetched => "fetched",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown WindowValueSource"),
    };
}

public sealed record DimensionResponse(
    string Id,
    string Label,
    string Algebra,
    IReadOnlyList<string> Values,
    string? TaskDefault,
    string? WindowDefault,
    string Source);

public sealed record ClaimingDimensionResponse(string? DimensionId);
public sealed record LooseTagsResponse(IReadOnlyList<string> Tags, int Count);
