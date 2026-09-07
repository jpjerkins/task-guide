using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.Domain.Time;

namespace TaskGuide.Domain.Ranking;

/// <summary>
/// The live, read-only warning for a schedule change that turns an admitting Task into an Orphan.
/// Both Pattern switching and removing a Window value ask this same counterfactual question.
/// </summary>
public static class Drift
{
    public static int CountNewlyOrphaned(
        IReadOnlyList<TaskItem> tasks,
        Func<TaskId, CompletionLog> completionsFor,
        Pattern current,
        Pattern candidate,
        IReadOnlyList<DayTemplate> currentTemplates,
        IReadOnlyList<DayTemplate> candidateTemplates,
        OpportunityCounter opportunities,
        DimensionRegistry registry,
        StaleThresholds staleThresholds,
        DateTimeOffset now,
        DayBoundary boundary)
    {
        var weekOf = boundary.DateOf(now).AddDays(-(int)boundary.DateOf(now).DayOfWeek);

        return tasks.Count(task =>
        {
            var status = StatusRules.Of(task, completionsFor(task.Id), registry, staleThresholds, now, boundary);
            if (status != Status.Active) return false;

            var currentIsOrphan = OrphanDetection.IsTaskOrphan(status, opportunities.CountInPatternWeek(task, current, currentTemplates, weekOf));
            var candidateIsOrphan = OrphanDetection.IsTaskOrphan(status, opportunities.CountInPatternWeek(task, candidate, candidateTemplates, weekOf));
            return !currentIsOrphan && candidateIsOrphan;
        });
    }

    public static IReadOnlyList<WindowValueDependents> DependentsForRemovingEachValue(
        IReadOnlyList<TaskItem> tasks,
        Func<TaskId, CompletionLog> completionsFor,
        Pattern activePattern,
        IReadOnlyList<DayTemplate> templates,
        DayTemplateId templateId,
        WindowId windowId,
        OpportunityCounter opportunities,
        DimensionRegistry registry,
        StaleThresholds staleThresholds,
        DateTimeOffset now,
        DayBoundary boundary)
    {
        var template = templates.Single(candidate => candidate.Id.Equals(templateId));
        var window = template.Windows.Single(candidate => candidate.Id.Equals(windowId));

        return window.Tags.Dimensions
            .SelectMany(pair => pair.Value.Distinct().Select(value => (pair.Key, value)))
            .OrderBy(value => value.Key.Value, StringComparer.Ordinal)
            .ThenBy(value => value.value.Value, StringComparer.Ordinal)
            .Select(value => new WindowValueDependents(
                value.Key,
                value.value,
                CountNewlyOrphaned(
                    tasks,
                    completionsFor,
                    activePattern,
                    activePattern,
                    templates,
                    ReplaceValue(templates, templateId, windowId, value.Key, value.value),
                    opportunities,
                    registry,
                    staleThresholds,
                    now,
                    boundary)))
            .ToArray();
    }

    private static IReadOnlyList<DayTemplate> ReplaceValue(
        IReadOnlyList<DayTemplate> templates,
        DayTemplateId templateId,
        WindowId windowId,
        DimensionId dimensionId,
        TagValue value) =>
        templates.Select(template => !template.Id.Equals(templateId)
            ? template
            : template with
            {
                Windows = template.Windows.Select(window => !window.Id.Equals(windowId)
                    ? window
                    : window with { Tags = Without(window.Tags, dimensionId, value) }).ToArray(),
            }).ToArray();

    private static TagSet Without(TagSet tags, DimensionId dimensionId, TagValue value) =>
        new(tags.Dimensions.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<TagValue>)(pair.Key.Equals(dimensionId)
                ? pair.Value.Where(candidate => !candidate.Equals(value)).ToArray()
                : pair.Value)), tags.LooseTags);
}

public sealed record WindowValueDependents(DimensionId DimensionId, TagValue Value, int DependentTasks);
