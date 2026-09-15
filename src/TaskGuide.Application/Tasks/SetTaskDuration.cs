using OneOf;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Matching;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;

namespace TaskGuide.Application.Tasks;

/// <summary>Sets the authored Duration fact; Status remains derived on subsequent reads.</summary>
public sealed class SetTaskDuration(IStore store)
{
    public async Task<OneOf<DurationSet, InvalidTaskDuration, SetTaskDurationRefused>> ExecuteAsync(
        TaskId id, string? duration, CancellationToken cancellationToken)
    {
        if (!KnownDimensions.DurationBuckets.Any(value => value.Value == duration))
            return new InvalidTaskDuration("duration must be one of the declared Duration buckets");
        var bucket = KnownDimensions.DurationBuckets.Single(value => value.Value == duration);
        var canonical = int.TryParse(bucket.Value, out var minutes)
            ? DurationCeiling.SnapUp(minutes, KnownDimensions.DurationBuckets)
            : bucket;
        var result = await store.MutateAsync<SetTaskDurationRefused>(view =>
        {
            var task = view.Tasks.SingleOrDefault(candidate => candidate.Id.Equals(id));
            if (task is null) return new SetTaskDurationRefused("Stored Task was not found");
            if (task.Provenance is not null) return new SetTaskDurationRefused("A derived Task cannot have its Duration edited");
            var dimensions = task.Tags.Dimensions.ToDictionary(pair => pair.Key, pair => pair.Value);
            dimensions[KnownDimensions.Duration] = [canonical];
            var updated = task with { Tags = task.Tags with { Dimensions = dimensions } };
            return OneOf<StoreMutation, SetTaskDurationRefused>.FromT0(new StoreMutation([
                new TasksWrite((IReadOnlyList<TaskItem>)[.. view.Tasks.Select(candidate => candidate.Id.Equals(id) ? updated : candidate)])]));
        }, cancellationToken);
        return result.Match<OneOf<DurationSet, InvalidTaskDuration, SetTaskDurationRefused>>(
            _ => new DurationSet(), refusal => refusal);
    }
}

public sealed record DurationSet;
public sealed record InvalidTaskDuration(string Reason);
public sealed record SetTaskDurationRefused(string Reason);
