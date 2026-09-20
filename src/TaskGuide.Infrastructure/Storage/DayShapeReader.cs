using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;

namespace TaskGuide.Infrastructure.Storage;

/// <summary>
/// Safe to register as a singleton: caches nothing and never writes — every <see cref="For"/>
/// call re-reads <paramref name="store"/>'s current view, so it always sees whatever the store
/// holds at call time.
/// </summary>
public sealed class DayShapeReader(IStoreReader store) : IDayShapeReader
{
    public DayShape For(DateOnly date)
    {
        var view = store.Read();
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
            .Concat(dateOverride?.Events ?? RecurringEvents.On(date, template.EventPrototypes, view.EventExceptions))
            .ToArray();

        return new DayShape(date, windows, events, IsOverridden: dateOverride is not null);
    }
}
