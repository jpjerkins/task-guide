using OneOf;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Matching;
using TaskGuide.Domain.Notifications;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;

namespace TaskGuide.Application.Capture;

/// <summary>Writes a captured Task before attempting its optional confirmation Receipt.</summary>
public sealed class CaptureTask(IStore store, IIdMinter minter, IReceiptSender receipts, TimeProvider timeProvider)
{
    public async Task<TaskItem> ExecuteAsync(CaptureTaskRequest request, Func<TaskId, Uri> detailPageFor, CancellationToken cancellationToken)
    {
        TagValue? duration = request.Duration is { } rawMinutes
            ? DurationCeiling.SnapUp(rawMinutes, KnownDimensions.DurationBuckets)
            : null;
        var task = new TaskItem(
            minter.NextTaskId(),
            request.Title,
            Notes: null,
            Tags: duration is null
                ? TagSet.Empty
                : new TagSet(new Dictionary<DimensionId, IReadOnlyList<TagValue>>
                {
                    [KnownDimensions.Duration] = [duration.Value],
                }, LooseTags: []),
            Deadline: null,
            Defer: null,
            Postpone: null,
            Recurrence: null,
            timeProvider.GetUtcNow());

        await store.MutateAsync<Never>(
            view => OneOf<StoreMutation, Never>.FromT0(
                new StoreMutation([new TasksWrite((IReadOnlyList<TaskItem>)[.. view.Tasks, task])])),
            cancellationToken);

        if (ReceiptPolicy.IsEarned(request.Source))
        {
            try
            {
                await receipts.SendReceiptAsync(
                    new Receipt(task.Id, task.Title, duration?.Value ?? "", detailPageFor(task.Id)),
                    cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // A Receipt is confirmation, not the capture itself. Expected delivery failures
                // are already returned and logged by the adapter; an unexpected one must not
                // turn a durably captured Task into a failed request either.
            }
        }

        return task;
    }
}

public sealed record CaptureTaskRequest(string Title, int? Duration, string Source);
