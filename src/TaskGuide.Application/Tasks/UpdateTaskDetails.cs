using OneOf;
using TaskGuide.Application.Ports;
using TaskGuide.Application.Rules;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Matching;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;

namespace TaskGuide.Application.Tasks;

/// <summary>Replaces the facts authored by the Task detail form in one in-lock mutation.</summary>
public sealed class UpdateTaskDetails(IStore store, DimensionRegistry registry, DerivedTaskComposer derivedTasks)
{
    public async Task<OneOf<TaskDetailsUpdated, InvalidTaskDetails, TaskDetailsRefused>> ExecuteAsync(
        TaskId id, TaskDetails details, CancellationToken cancellationToken)
    {
        if (Invalid(details) is { } invalid)
        {
            return invalid;
        }

        var result = await store.MutateAsync<TaskDetailsRefused>(view =>
        {
            var task = derivedTasks.Compose(view).SingleOrDefault(candidate => candidate.Id.Equals(id));
            if (task is null) return new TaskDetailsRefused("Task was not found");
            if (task.Provenance is not null) return new TaskDetailsRefused("A derived Task cannot be edited");
            if (task.Recurrence is not null && details.Deadline is not null)
            {
                return new TaskDetailsRefused("A recurring Task cannot have an authored Deadline");
            }

            var tags = new Dictionary<DimensionId, IReadOnlyList<TagValue>>();
            foreach (var (idValue, values) in details.Dimensions)
            {
                tags[new DimensionId(idValue)] = [.. values.Select(value => new TagValue(value))];
            }

            if (details.Duration is not null)
            {
                tags[KnownDimensions.Duration] = [CanonicalDuration(details.Duration)];
            }

            var updated = task with
            {
                Title = details.Title,
                Notes = details.Notes,
                Tags = new TagSet(tags, task.Tags.LooseTags),
                Deadline = details.Deadline,
            };
            return OneOf<StoreMutation, TaskDetailsRefused>.FromT0(new StoreMutation([
                new TasksWrite((IReadOnlyList<TaskItem>)[.. view.Tasks.Select(candidate => candidate.Id.Equals(id) ? updated : candidate)]),
            ]));
        }, cancellationToken);

        return result.Match<OneOf<TaskDetailsUpdated, InvalidTaskDetails, TaskDetailsRefused>>(
            _ => new TaskDetailsUpdated(),
            refusal => refusal);
    }

    private InvalidTaskDetails? Invalid(TaskDetails details)
    {
        if (string.IsNullOrWhiteSpace(details.Title)) return new InvalidTaskDetails("title is required");
        if (details.Dimensions.ContainsKey(KnownDimensions.Duration.Value))
        {
            return new InvalidTaskDetails("duration belongs in the duration field");
        }

        if (details.Duration is not null && !KnownDimensions.DurationBuckets.Any(value => value.Value == details.Duration))
        {
            return new InvalidTaskDetails("duration must be one of the declared Duration buckets");
        }

        foreach (var (id, values) in details.Dimensions)
        {
            var dimension = registry.Dimensions.SingleOrDefault(candidate => candidate.Id.Value == id);
            if (dimension is null) return new InvalidTaskDetails($"dimension '{id}' is not declared");
            if (values.Count != values.Distinct(StringComparer.Ordinal).Count())
            {
                return new InvalidTaskDetails($"dimension '{id}' contains duplicate values");
            }

            if (dimension.Value is OrdinalDimension && values.Count > 1)
            {
                return new InvalidTaskDetails($"ordinal dimension '{id}' accepts at most one value");
            }

            if (values.Any(value => !dimension.Values.Any(declared => declared.Value == value)))
            {
                return new InvalidTaskDetails($"dimension '{id}' contains an undeclared value");
            }
        }

        return null;
    }

    private static TagValue CanonicalDuration(string duration)
    {
        var bucket = KnownDimensions.DurationBuckets.Single(value => value.Value == duration);
        return int.TryParse(bucket.Value, out var minutes)
            ? DurationCeiling.SnapUp(minutes, KnownDimensions.DurationBuckets)
            : bucket;
    }
}

public sealed record TaskDetails(
    string Title,
    string? Notes,
    string? Duration,
    DateOnly? Deadline,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Dimensions);

public sealed record TaskDetailsUpdated;
public sealed record InvalidTaskDetails(string Reason);
public sealed record TaskDetailsRefused(string Reason);
