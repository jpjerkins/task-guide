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
        dimensions.MapGet("/", () => Results.NoContent());

        // Which Dimension will claim a string as it is typed, or plainly that nothing will. This
        // is the only moment the system can catch a mistyped Tag, which is otherwise invisible to
        // every mechanism in the model: `#garge` claims nothing, so it admits the Task to MORE
        // Windows than the Tag its author meant.
        dimensions.MapGet("/claiming", (string tag) => Results.NoContent());

        // The inert-tags staging area, with its count — tags resolving to no Dimension.
        dimensions.MapGet("/loose-tags", () => Results.NoContent());

        return dimensions;
    }
}
