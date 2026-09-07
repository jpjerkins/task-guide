using OneOf;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Time;

namespace TaskGuide.Application.Schedule;

public sealed class DeleteDayTemplate(IStore store, TimeProvider timeProvider, DayBoundary boundary)
{
    public async Task<DeleteDayTemplateOutcome> ExecuteAsync(
        DayTemplateId templateId,
        CancellationToken cancellationToken)
    {
        var outcome = await store.MutateAsync<DeleteDayTemplateRefused>(view =>
        {
            var template = view.DayTemplates.SingleOrDefault(candidate => candidate.Id.Equals(templateId));
            if (template is null)
            {
                return new DeleteDayTemplateRefused("Day template was not found");
            }

            var today = boundary.DateOf(timeProvider.GetUtcNow());
            if (!DayTemplateLifecycle.IsUnused(templateId, view.Patterns.Patterns, view.Overrides, today))
            {
                return new DeleteDayTemplateRefused("Day template is in use");
            }

            return OneOf<StoreMutation, DeleteDayTemplateRefused>.FromT0(new StoreMutation([
                new DayTemplatesWrite(DayTemplateLifecycle.Delete(templateId, view.DayTemplates)),
            ]));
        }, cancellationToken);

        return outcome.Match<DeleteDayTemplateOutcome>(
            _ => new DeletedDayTemplate(),
            refusal => refusal);
    }
}

[GenerateOneOf]
public partial class DeleteDayTemplateOutcome : OneOfBase<DeletedDayTemplate, DeleteDayTemplateRefused>;

public sealed record DeletedDayTemplate;
public sealed record DeleteDayTemplateRefused(string Reason);
