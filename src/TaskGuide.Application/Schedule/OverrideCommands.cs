using OneOf;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;

namespace TaskGuide.Application.Schedule;

public sealed class CreateOverrideSpan(IStore store)
{
    public async Task<OverrideSpanOutcome> ExecuteAsync(
        OverrideSpanCommandRequest request,
        CancellationToken cancellationToken)
    {
        if (request.To < request.From)
        {
            return new OverrideSpanRefused("The end date must not precede the start date");
        }

        var outcome = await store.MutateAsync<OverrideSpanRefused>(view =>
        {
            var replacements = request.Mode.Match<OneOf<IReadOnlyList<DateOverride>, OverrideSpanRefused>>(
                stamp =>
                {
                    var template = view.DayTemplates.SingleOrDefault(candidate => candidate.Id.Equals(stamp.TemplateId));
                    return template is null
                        ? OneOf<IReadOnlyList<DateOverride>, OverrideSpanRefused>.FromT1(new OverrideSpanRefused("Day template was not found"))
                        : OneOf<IReadOnlyList<DateOverride>, OverrideSpanRefused>.FromT0(
                            request.Dates().Select(date => DayTemplateLifecycle.Stamp(date, template)).ToArray());
                },
                freeze => OneOf<IReadOnlyList<DateOverride>, OverrideSpanRefused>.FromT0(
                    request.Dates().Select(date => Freeze(date, view)).ToArray()),
                blank => OneOf<IReadOnlyList<DateOverride>, OverrideSpanRefused>.FromT0(
                    request.Dates().Select(date => new DateOverride(date, [], null)).ToArray()));

            if (replacements.TryPickT1(out var refusal, out var datedOverrides))
            {
                return refusal;
            }

            var dates = datedOverrides.Select(overrideDay => overrideDay.Date).ToHashSet();
            return OneOf<StoreMutation, OverrideSpanRefused>.FromT0(new StoreMutation([
                new OverridesWrite([.. view.Overrides.Where(overrideDay => !dates.Contains(overrideDay.Date)), .. datedOverrides]),
            ]));
        }, cancellationToken);

        return outcome.Match<OverrideSpanOutcome>(_ => new OverrideSpanApplied(), refusal => refusal);
    }

    private static DateOverride Freeze(DateOnly date, IStoreView view)
    {
        var existing = view.Overrides.SingleOrDefault(overrideDay => overrideDay.Date == date);
        var windows = existing?.Windows;
        if (windows is null)
        {
            var templateId = view.Patterns.Active[date.DayOfWeek];
            var template = view.DayTemplates.SingleOrDefault(candidate => candidate.Id.Equals(templateId))
                ?? throw new InvalidOperationException(
                    $"Day template {templateId.Value} does not match the active Pattern for {date:yyyy-MM-dd}.");
            windows = template.Windows;
        }

        return new DateOverride(date, [.. windows], existing?.Used);
    }
}

[GenerateOneOf]
public partial class OverrideSpanMode : OneOfBase<StampOverrideSpan, FreezeOverrideSpan, BlankOverrideSpan>;

public sealed record StampOverrideSpan(DayTemplateId TemplateId);
public sealed record FreezeOverrideSpan;
public sealed record BlankOverrideSpan;

public sealed record OverrideSpanCommandRequest(DateOnly From, DateOnly To, OverrideSpanMode Mode)
{
    public IEnumerable<DateOnly> Dates()
    {
        for (var date = From; ; date = date.AddDays(1))
        {
            yield return date;
            if (date == To) yield break;
        }
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
