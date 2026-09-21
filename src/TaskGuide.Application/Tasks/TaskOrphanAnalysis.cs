using TaskGuide.Application.Ports;
using TaskGuide.Application.Rules;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Ranking;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Time;
using TaskGuide.Domain.Tasks;

namespace TaskGuide.Application.Tasks;

/// <summary>
/// Explains an active Task's structural orphan state without treating a conjunction failure as
/// blame on one axis. The result is for read adapters; no orphan fact is stored.
/// </summary>
public sealed class TaskOrphanAnalysis(
    DimensionRegistry registry,
    StaleThresholds staleThresholds,
    DayBoundary boundary,
    OpportunityCounter opportunities)
{
    public OrphanAnalysisResult Analyze(
        TaskItem task,
        IStoreView view,
        DateTimeOffset now,
        Status? knownStatus = null,
        int? knownPatternWeekCount = null)
    {
        var status = knownStatus ?? StatusRules.Of(task, view.CompletionsFor(task.Id), registry, staleThresholds, now, boundary);
        // Matching and orphan analysis are undefined until Duration exists; the status gate is
        // deliberately before any opportunity count so an Unprocessed Task never reaches Matcher.
        if (status is not Status.Active)
        {
            return OrphanAnalysisResult.Empty;
        }

        var weekOf = boundary.DateOf(now);
        var patternWeekCount = knownPatternWeekCount ?? opportunities.CountInPatternWeek(
            task, view.Patterns.Active, view.DayTemplates, weekOf);
        if (!OrphanDetection.IsTaskOrphan(status, patternWeekCount))
        {
            return OrphanAnalysisResult.Empty;
        }

        var blame = task.Tags.Dimensions
            .Where(pair => pair.Value.Count > 0)
            .Where(pair => opportunities.CountInPatternWeek(
                TaskConstrainedOnlyBy(task, pair.Key),
                view.Patterns.Active,
                view.DayTemplates,
                weekOf) == 0)
            .Select(pair => pair.Key.Value)
            .ToArray();

        var repairTemplates = view.Patterns.Active.Days.Distinct()
            .Select(id => view.DayTemplates.Single(template => template.Id == id))
            .Where(template => blame.Any(dimension => !TemplateAdmitsDimension(task, dimension, template, weekOf)))
            .Select(template => template.Id.Value)
            .ToArray();

        return new OrphanAnalysisResult(blame, repairTemplates);
    }

    private bool TemplateAdmitsDimension(
        TaskItem task,
        string dimension,
        DayTemplate template,
        DateOnly weekOf)
    {
        var axisTask = TaskConstrainedOnlyBy(task, new DimensionId(dimension));
        var pattern = new Pattern(new PatternId("p_orphan_repair"), "Orphan repair", [.. Enumerable.Repeat(template.Id, 7)]);
        return opportunities.CountInPatternWeek(axisTask, pattern, [template], weekOf) > 0;
    }

    // Duration has no default. For a non-Duration axis we provide only the least restrictive
    // valid Duration bucket so the question stays well-formed without smuggling a conjunction
    // constraint into the independent-axis check.
    private static TaskItem TaskConstrainedOnlyBy(TaskItem task, DimensionId dimension) =>
        task with
        {
            Tags = new TagSet(
                dimension == KnownDimensions.Duration
                    ? new Dictionary<DimensionId, IReadOnlyList<TagValue>>
                    {
                        [dimension] = task.Tags.On(dimension),
                    }
                    : new Dictionary<DimensionId, IReadOnlyList<TagValue>>
                    {
                        [KnownDimensions.Duration] = [KnownDimensions.DurationBuckets[0]],
                        [dimension] = task.Tags.On(dimension),
                    },
                []),
        };
}

public sealed record OrphanAnalysisResult(
    IReadOnlyList<string> BlameDimensionIds,
    IReadOnlyList<string> RepairTemplateIds)
{
    public static OrphanAnalysisResult Empty { get; } = new([], []);
}
