using OneOf;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Time;

namespace TaskGuide.Application.RightNow;

/// <summary>
/// Persists the dimension values the user observes to admit Tasks right now. The first adjustment
/// stamps the active Pattern's day template; subsequent adjustments replace that date's one
/// Override rather than layering another one over it.
/// </summary>
public sealed class MatchingOn(IStore store, TimeProvider timeProvider, DayBoundary boundary)
{
    public async Task<OneOf<MatchingOnApplied, MatchingOnRefused>> ExecuteAsync(
        MatchingOnRequest request,
        CancellationToken cancellationToken)
    {
        var result = await store.MutateAsync<MatchingOnRefused>(view =>
        {
            if (timeProvider.GetUtcNow() >= boundary.EndOf(request.Date))
            {
                return new MatchingOnRefused("This reminder was for yesterday");
            }

            var existing = view.Overrides.SingleOrDefault(candidate => candidate.Date == request.Date);
            var source = existing is null ? SourceFromActivePattern(view, request.Date) : new OverrideSource(existing.Windows, existing.Used);
            var target = source.Windows.SingleOrDefault(window => window.Id.Equals(request.WindowId));
            if (target is null)
            {
                return new MatchingOnRefused("Window was not found on that date");
            }

            var adjusted = target with { Tags = new TagSet(request.Dimensions, target.Tags.LooseTags) };
            var replacement = new DateOverride(
                request.Date,
                source.Windows.Select(window => window.Id.Equals(request.WindowId) ? adjusted : window).ToArray(),
                source.Used);
            var overrides = existing is null
                ? (IReadOnlyList<DateOverride>)[.. view.Overrides, replacement]
                : [.. view.Overrides.Select(candidate => candidate.Date == request.Date ? replacement : candidate)];

            return OneOf<StoreMutation, MatchingOnRefused>.FromT0(new StoreMutation([new OverridesWrite(overrides)]));
        }, cancellationToken);

        return result.Match<OneOf<MatchingOnApplied, MatchingOnRefused>>(
            _ => new MatchingOnApplied(),
            refusal => refusal);
    }

    private static OverrideSource SourceFromActivePattern(IStoreView view, DateOnly date)
    {
        var templateId = view.Patterns.Active[date.DayOfWeek];
        var template = view.DayTemplates.SingleOrDefault(candidate => candidate.Id.Equals(templateId))
            ?? throw new InvalidOperationException($"Active Pattern names missing Day template {templateId.Value}");

        return new OverrideSource(template.Windows, new DayTemplateUse(template.Id, template.Name));
    }

    private sealed record OverrideSource(IReadOnlyList<AvailabilityWindow> Windows, DayTemplateUse? Used);
}

public sealed record MatchingOnRequest(
    DateOnly Date,
    WindowId WindowId,
    IReadOnlyDictionary<DimensionId, IReadOnlyList<TagValue>> Dimensions);

public sealed record MatchingOnApplied;
public sealed record MatchingOnRefused(string Reason);
