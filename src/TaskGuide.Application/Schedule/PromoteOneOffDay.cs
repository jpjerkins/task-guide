using OneOf;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;

namespace TaskGuide.Application.Schedule;

public sealed class PromoteOneOffDay(IStore store)
{
    public async Task<PromoteOneOffDayOutcome> ExecuteAsync(
        DateOnly sourceDate,
        DayTemplate template,
        CancellationToken cancellationToken)
    {
        var outcome = await store.MutateAsync<PromoteOneOffDayRefused>(view =>
        {
            var source = view.Overrides.SingleOrDefault(overrideDay => overrideDay.Date == sourceDate);
            if (source is null)
            {
                return new PromoteOneOffDayRefused("The source date has no Override");
            }

            if (!source.IsOneOffDay)
            {
                return new PromoteOneOffDayRefused("Only a one-off day can be promoted");
            }

            var promoted = DayTemplateLifecycle.Promote(source, template);
            return OneOf<StoreMutation, PromoteOneOffDayRefused>.FromT0(new StoreMutation([
                new OverridesWrite([.. view.Overrides.Where(overrideDay => overrideDay.Date != sourceDate), promoted.Source]),
                // The use record names the template. Persist it first so a crash leaves a detectable
                // dangling reference, never an apparently-unused template that can be silently deleted.
                new DayTemplatesWrite([.. view.DayTemplates, promoted.Template]),
            ]));
        }, cancellationToken);

        return outcome.Match<PromoteOneOffDayOutcome>(
            _ => store.Read().DayTemplates.Single(candidate => candidate.Id.Equals(template.Id)),
            refusal => refusal);
    }
}

[GenerateOneOf]
public partial class PromoteOneOffDayOutcome : OneOfBase<DayTemplate, PromoteOneOffDayRefused>;

public sealed record PromoteOneOffDayRefused(string Reason);
