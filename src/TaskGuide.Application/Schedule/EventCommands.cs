using OneOf;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;

namespace TaskGuide.Application.Schedule;

public sealed class CreateEvent(IStore store, IIdMinter minter)
{
    public async Task<CreateEventOutcome> ExecuteAsync(
        Event @event,
        IReadOnlyDictionary<WindowId, OverlapResolution> resolutions,
        CancellationToken cancellationToken)
    {
        var outcome = await store.MutateAsync<CreateEventRefused>(view =>
        {
            var overlapping = EventScheduling.WindowsOn(view, @event.Date)
                .Where(window => Overlaps(window, @event))
                .ToArray();

            if (overlapping.Any(window => !resolutions.ContainsKey(window.Id)))
            {
                return new CreateEventRefused("Every overlapping Window needs a resolution");
            }

            if (overlapping.Any(window => !CanResolve(window, @event, resolutions[window.Id])))
            {
                return new CreateEventRefused("The overlap resolution cannot preserve that Window");
            }

            if (overlapping.Length == 0)
            {
                return OneOf<StoreMutation, CreateEventRefused>.FromT0(new StoreMutation([
                    new EventsWrite([.. view.Events, @event]),
                ]));
            }

            var replacement = EventScheduling.WindowsOn(view, @event.Date)
                .SelectMany(window => resolutions.TryGetValue(window.Id, out var resolution)
                    ? Resolve(window, @event, resolution)
                    : [window])
                .ToArray();
            var existing = view.Overrides.SingleOrDefault(day => day.Date == @event.Date);
            var overrideDay = new DateOverride(@event.Date, replacement, existing?.Used);

            // The Event is deliberately first: a crash leaves the detectable half of the
            // interaction, so overlap-check can re-offer the missing one-off day.
            return OneOf<StoreMutation, CreateEventRefused>.FromT0(new StoreMutation([
                new EventsWrite([.. view.Events, @event]),
                new OverridesWrite([.. view.Overrides.Where(day => day.Date != @event.Date), overrideDay]),
            ]));
        }, cancellationToken);

        return outcome.Match<CreateEventOutcome>(_ => new EventCreated(@event), refusal => refusal);
    }

    private static bool Overlaps(AvailabilityWindow window, Event @event) =>
        window.Start < @event.End && @event.Start < window.End;

    private static bool CanResolve(AvailabilityWindow window, Event @event, OverlapResolution resolution) =>
        resolution switch
        {
            OverlapResolution.Replace => true,
            OverlapResolution.TruncateStart => @event.End < window.End,
            OverlapResolution.TruncateEnd => window.Start < @event.Start,
            OverlapResolution.Split => window.Start < @event.Start && @event.End < window.End,
            _ => false,
        };

    private IEnumerable<AvailabilityWindow> Resolve(AvailabilityWindow window, Event @event, OverlapResolution resolution) =>
        resolution switch
        {
            OverlapResolution.Replace => [],
            OverlapResolution.TruncateStart when @event.End < window.End =>
                [window with { Start = @event.End }],
            OverlapResolution.TruncateEnd when window.Start < @event.Start =>
                [window with { End = @event.Start }],
            OverlapResolution.Split =>
            [
                window with { End = @event.Start },
                window with { Id = minter.NextWindowId(), Start = @event.End },
            ],
            _ => throw new InvalidOperationException("Overlap resolutions are validated before they are applied."),
        };
}

public sealed class EditEventException(IStore store)
{
    public async Task ExecuteAsync(EventException exception, CancellationToken cancellationToken) =>
        await store.MutateAsync<Never>(view => OneOf<StoreMutation, Never>.FromT0(new StoreMutation([
            new EventExceptionsWrite([
                .. view.EventExceptions.Where(existing => existing.Date != exception.Date || !existing.PrototypeId.Equals(exception.PrototypeId)),
                exception,
            ]),
        ])), cancellationToken);
}

public sealed class DeleteEventException(IStore store)
{
    public async Task ExecuteAsync(DateOnly date, EventPrototypeId prototypeId, CancellationToken cancellationToken) =>
        await store.MutateAsync<Never>(view => OneOf<StoreMutation, Never>.FromT0(new StoreMutation([
            new EventExceptionsWrite([
                .. view.EventExceptions.Where(existing => existing.Date != date || !existing.PrototypeId.Equals(prototypeId)),
            ]),
        ])), cancellationToken);
}

public static class EventScheduling
{
    public static IReadOnlyList<AvailabilityWindow> WindowsOn(IStoreView view, DateOnly date)
    {
        var stamped = view.Overrides.SingleOrDefault(day => day.Date == date);
        if (stamped is not null) return stamped.Windows;

        var templateId = view.Patterns.Active[date.DayOfWeek];
        return view.DayTemplates.SingleOrDefault(template => template.Id.Equals(templateId))?.Windows ?? [];
    }
}

[GenerateOneOf]
public partial class CreateEventOutcome : OneOfBase<EventCreated, CreateEventRefused>;

public sealed record EventCreated(Event Event);
public sealed record CreateEventRefused(string Reason);
