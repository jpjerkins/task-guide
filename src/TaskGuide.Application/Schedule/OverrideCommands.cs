using OneOf;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Schedule;

namespace TaskGuide.Application.Schedule;

public sealed class CreateOverrideSpan(IStore store)
{
    public async Task<OverrideSpanOutcome> ExecuteAsync(OverrideSpanRequest request, CancellationToken cancellationToken)
    {
        if (request.To < request.From)
        {
            return new OverrideSpanRefused("The end date must not precede the start date");
        }

        var outcome = await store.MutateAsync<OverrideSpanRefused>(view =>
        {
            DayTemplate? template = null;
            if (request.Stamp is { } templateId)
            {
                template = view.DayTemplates.SingleOrDefault(candidate => candidate.Id.Equals(templateId));
                if (template is null) return new OverrideSpanRefused("Day template was not found");
            }

            var replacements = request.Dates()
                .Select(date => template is null
                    ? new DateOverride(date, [], null)
                    : DayTemplateLifecycle.Stamp(date, template))
                .ToArray();
            var dates = replacements.Select(overrideDay => overrideDay.Date).ToHashSet();
            return OneOf<StoreMutation, OverrideSpanRefused>.FromT0(new StoreMutation([
                new OverridesWrite([.. view.Overrides.Where(overrideDay => !dates.Contains(overrideDay.Date)), .. replacements]),
            ]));
        }, cancellationToken);

        return outcome.Match<OverrideSpanOutcome>(_ => new OverrideSpanApplied(), refusal => refusal);
    }
}

public sealed class EditOverride(IStore store)
{
    public async Task<EditOverrideOutcome> ExecuteAsync(
        DateOnly date,
        IReadOnlyList<AvailabilityWindow> windows,
        CancellationToken cancellationToken)
    {
        var outcome = await store.MutateAsync<OverrideNotFound>(view =>
        {
            var existing = view.Overrides.SingleOrDefault(candidate => candidate.Date == date);
            if (existing is null) return new OverrideNotFound("Override was not found");

            var edited = existing with { Windows = [.. windows] };
            return OneOf<StoreMutation, OverrideNotFound>.FromT0(new StoreMutation([
                new OverridesWrite([.. view.Overrides.Select(candidate => candidate.Date == date ? edited : candidate)]),
            ]));
        }, cancellationToken);

        return outcome.Match<EditOverrideOutcome>(_ => new OverrideEdited(), refusal => refusal);
    }
}

public sealed class DeleteOverride(IStore store)
{
    public async Task<DeleteOverrideOutcome> ExecuteAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var outcome = await store.MutateAsync<OverrideNotFound>(view =>
        {
            var existing = view.Overrides.SingleOrDefault(candidate => candidate.Date == date);
            if (existing is null) return new OverrideNotFound("Override was not found");

            return OneOf<StoreMutation, OverrideNotFound>.FromT0(new StoreMutation([
                new OverridesWrite([.. view.Overrides.Where(candidate => candidate.Date != date)]),
            ]));
        }, cancellationToken);

        return outcome.Match<DeleteOverrideOutcome>(_ => new OverrideDeleted(), refusal => refusal);
    }
}

[GenerateOneOf]
public partial class OverrideSpanOutcome : OneOfBase<OverrideSpanApplied, OverrideSpanRefused>;

public sealed record OverrideSpanApplied;
public sealed record OverrideSpanRefused(string Reason);

[GenerateOneOf]
public partial class EditOverrideOutcome : OneOfBase<OverrideEdited, OverrideNotFound>;

public sealed record OverrideEdited;
public sealed record OverrideNotFound(string Reason);

[GenerateOneOf]
public partial class DeleteOverrideOutcome : OneOfBase<OverrideDeleted, OverrideNotFound>;

public sealed record OverrideDeleted;
