using OneOf;
using TaskGuide.Application.Ports;
using TaskGuide.Application.Rules;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Tasks;

namespace TaskGuide.Application.Tasks;

/// <summary>Persists the absolute date selected by the reactive “Not now” gesture.</summary>
public sealed class PostponeTask(IStore store, DerivedTaskComposer derivedTasks)
{
    public async Task<OneOf<Postponed, PostponeRefused>> ExecuteAsync(TaskId id, DateOnly until, CancellationToken cancellationToken)
    {
        var result = await store.MutateAsync<PostponeRefused>(view =>
        {
            var task = derivedTasks.Compose(view).SingleOrDefault(candidate => candidate.Id.Equals(id));
            if (task is null)
            {
                return new PostponeRefused("Task was not found");
            }

            if (!StatusRules.CanPostpone(task))
            {
                return new PostponeRefused("Recurring and derived Tasks cannot be postponed");
            }

            var updated = task with { Postpone = until };
            return OneOf<StoreMutation, PostponeRefused>.FromT0(
                new StoreMutation([new TasksWrite((IReadOnlyList<TaskItem>)[.. view.Tasks.Select(candidate => candidate.Id.Equals(id) ? updated : candidate)])]));
        }, cancellationToken);

        return result.Match<OneOf<Postponed, PostponeRefused>>(
            _ => new Postponed(),
            refusal => refusal);
    }
}

public sealed record Postponed;
public sealed record PostponeRefused(string Reason);
