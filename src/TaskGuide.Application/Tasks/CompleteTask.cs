using OneOf;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Tasks;
using TaskGuide.Domain.Time;

namespace TaskGuide.Application.Tasks;

/// <summary>Records the one authored completion fact for a Task's live instance.</summary>
public sealed class CompleteTask(
    IStore store,
    DimensionRegistry registry,
    StaleThresholds staleThresholds,
    TimeProvider timeProvider,
    DayBoundary boundary)
{
    public async Task<OneOf<CompletionRecorded, CompletionRefused>> ExecuteAsync(TaskId id, CancellationToken cancellationToken)
    {
        var result = await store.MutateAsync<CompletionRefused>(view =>
        {
            var task = view.Tasks.SingleOrDefault(candidate => candidate.Id.Equals(id));
            if (task is null)
            {
                return new CompletionRefused("Task was not found");
            }

            var now = timeProvider.GetUtcNow();
            var log = view.CompletionsFor(id);
            if (StatusRules.Of(task, log, registry, staleThresholds, now, boundary) is Status.Unprocessed)
            {
                return new CompletionRefused("An Unprocessed Task cannot be completed");
            }

            var due = task.Recurrence is { } recurrence
                ? RecurrenceRules.LiveInstanceDeadline(recurrence, task.CreatedAt, log, now, boundary)
                : task.Deadline;
            var entry = new CompletionEntry(due, now);
            var updated = task.Recurrence is null ? log.WithOnlyCompletion(entry) : log.With(entry);
            return OneOf<StoreMutation, CompletionRefused>.FromT0(new StoreMutation([new CompletionLogWrite(updated)]));
        }, cancellationToken);

        return result.Match<OneOf<CompletionRecorded, CompletionRefused>>(
            _ => new CompletionRecorded(),
            refusal => refusal);
    }
}

public sealed record CompletionRecorded;
public sealed record CompletionRefused(string Reason);
