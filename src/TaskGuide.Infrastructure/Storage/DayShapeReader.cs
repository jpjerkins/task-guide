using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;

namespace TaskGuide.Infrastructure.Storage;

public sealed class DayShapeReader(IStore store) : IDayShapeReader
{
    private readonly IStore _store = store;

    public DayShape For(DateOnly date)
    {
        var view = _store.Read();
        var active = view.Patterns.Active;
        var templateId = active[date.DayOfWeek];
        var template = view.DayTemplates.SingleOrDefault(t => t.Id == templateId)
            ?? throw new InvalidOperationException(
                $"Day template {templateId.Value} does not match any Day template in the store, " +
                $"but Pattern {active.Id.Value} names it for {date.DayOfWeek} ({date:yyyy-MM-dd}).");
        var dateOverride = view.Overrides.SingleOrDefault(o => o.Date == date);
        var windows = dateOverride?.Windows;
        windows ??= template.Windows;
        var events = view.Events
            .Where(e => e.Date == date)
            .Concat(template.EventPrototypes.SelectMany(p => RecurringInstance(view, date, p)))
            .ToArray();

        return new DayShape(date, windows, events, IsOverridden: dateOverride is not null);
    }

    private static IEnumerable<Event> RecurringInstance(IStoreView view, DateOnly date, EventPrototype prototype)
    {
        var exception = view.EventExceptions.SingleOrDefault(e => e.Date == date && e.PrototypeId == prototype.Id);
        if (exception?.Deleted == true)
        {
            yield break;
        }

        yield return new Event(
            RecurringEventId(date, prototype.Id),
            date,
            exception?.Name ?? prototype.Name,
            exception?.Start ?? prototype.Start,
            exception?.End ?? prototype.End,
            prototype.Tags,
            prototype.AbsenceNotice);
    }

    private static EventId RecurringEventId(DateOnly date, EventPrototypeId prototype) =>
        new($"evt_rec_{date:yyyyMMdd}_{prototype.Value}");
}
