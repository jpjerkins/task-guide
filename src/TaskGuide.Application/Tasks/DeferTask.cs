using OneOf;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Tasks;

namespace TaskGuide.Application.Tasks;

/// <summary>Changes the fact describing when a Task may first surface.</summary>
public sealed class DeferTask(IStore store)
{
    public async Task<OneOf<Deferred, DeferRefused>> ExecuteAsync(TaskId id, Defer defer, CancellationToken cancellationToken)
    {
        var result = await store.MutateAsync<DeferRefused>(view =>
        {
            var task = view.Tasks.SingleOrDefault(candidate => candidate.Id.Equals(id));
            if (task is null)
            {
                return new DeferRefused("Task was not found");
            }

            if (task.Recurrence is not null && defer.IsT0)
            {
                return new DeferRefused("A recurring Task requires an offset Defer");
            }

            if (task.Provenance is not null)
            {
                return new DeferRefused("A derived Task cannot be deferred");
            }

            var updated = task with { Defer = defer };
            return OneOf<StoreMutation, DeferRefused>.FromT0(
                new StoreMutation([new TasksWrite((IReadOnlyList<TaskItem>)[.. view.Tasks.Select(candidate => candidate.Id.Equals(id) ? updated : candidate)])]));
        }, cancellationToken);

        return result.Match<OneOf<Deferred, DeferRefused>>(
            _ => new Deferred(),
            refusal => refusal);
    }
}

public sealed record Deferred;
public sealed record DeferRefused(string Reason);
