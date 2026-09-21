using OneOf;
using TaskGuide.Application.Ports;
using TaskGuide.Application.Rules;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Tasks;

namespace TaskGuide.Application.Tasks;

/// <summary>Clears the stored Postpone fact on a Task that could have been postponed.</summary>
public sealed class ClearTaskPostpone(IStore store, DerivedTaskComposer derivedTasks)
{
    public async Task<OneOf<PostponeCleared, PostponeRefused>> ExecuteAsync(TaskId id, CancellationToken cancellationToken)
    {
        var result = await store.MutateAsync<PostponeRefused>(view =>
        {
            var task = derivedTasks.Compose(view).SingleOrDefault(candidate => candidate.Id.Equals(id));
            if (task is null) return new PostponeRefused("Task was not found");
            if (!StatusRules.CanPostpone(task)) return new PostponeRefused("Recurring and derived Tasks cannot be postponed");

            var updated = task with { Postpone = null };
            return OneOf<StoreMutation, PostponeRefused>.FromT0(new StoreMutation([
                new TasksWrite((IReadOnlyList<TaskItem>)[.. view.Tasks.Select(candidate => candidate.Id.Equals(id) ? updated : candidate)]),
            ]));
        }, cancellationToken);

        return result.Match<OneOf<PostponeCleared, PostponeRefused>>(_ => new PostponeCleared(), refusal => refusal);
    }
}

public sealed record PostponeCleared;
