using OneOf;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;

namespace TaskGuide.Application.Schedule;

public sealed class StampDayTemplate(IStore store)
{
    public async Task<StampDayTemplateOutcome> ExecuteAsync(
        DateOnly date,
        DayTemplateId templateId,
        CancellationToken cancellationToken)
    {
        var outcome = await store.MutateAsync<StampDayTemplateRefused>(view =>
        {
            var template = view.DayTemplates.SingleOrDefault(candidate => candidate.Id.Equals(templateId));
            if (template is null)
            {
                return new StampDayTemplateRefused("Day template was not found");
            }

            var stamped = DayTemplateLifecycle.Stamp(date, template);
            return OneOf<StoreMutation, StampDayTemplateRefused>.FromT0(new StoreMutation([
                new OverridesWrite([.. view.Overrides.Where(overrideDay => overrideDay.Date != date), stamped]),
            ]));
        }, cancellationToken);

        return outcome.Match<StampDayTemplateOutcome>(
            _ => DayTemplateLifecycle.Stamp(date, store.Read().DayTemplates.Single(template => template.Id.Equals(templateId))),
            refusal => refusal);
    }
}

[GenerateOneOf]
public partial class StampDayTemplateOutcome : OneOfBase<DateOverride, StampDayTemplateRefused>;

public sealed record StampDayTemplateRefused(string Reason);
