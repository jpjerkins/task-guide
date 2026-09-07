using TaskGuide.Application.Ports;
using TaskGuide.Application.Schedule;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.TestSupport;
using Xunit;

namespace TaskGuide.Application.Tests;

public sealed class EventCommandTests
{
    [Fact]
    public async Task An_Event_write_precedes_its_generated_Override_write()
    {
        var date = new DateOnly(2026, 10, 10);
        var window = new AvailabilityWindow(new WindowId("w_afternoon"), "Afternoon", new TimeOnly(13, 0), new TimeOnly(17, 0), TagSet.Empty);
        var store = new FakeStore(new FakeStoreViewBuilder()
            .WithOverrides([new DateOverride(date, [window], null)])
            .Build());
        var @event = new Event(new EventId("evt_tournament"), date, "Sam's tournament", new TimeOnly(14, 0), new TimeOnly(16, 0), TagSet.Empty, null);

        await new CreateEvent(store, new TestIdMinter()).ExecuteAsync(
            @event,
            new Dictionary<WindowId, OverlapResolution> { [window.Id] = OverlapResolution.Split },
            CancellationToken.None);

        var mutation = Assert.Single(store.Mutations);
        Assert.IsType<EventsWrite>(mutation.OrderedWrites[0]);
        Assert.IsType<OverridesWrite>(mutation.OrderedWrites[1]);
    }

    private sealed class TestIdMinter : IIdMinter
    {
        public TaskId NextTaskId() => throw new NotSupportedException();
        public WindowId NextWindowId() => new("w_split");
        public DayTemplateId NextDayTemplateId() => throw new NotSupportedException();
        public PatternId NextPatternId() => throw new NotSupportedException();
        public EventId NextEventId() => throw new NotSupportedException();
        public EventPrototypeId NextEventPrototypeId() => throw new NotSupportedException();
    }
}
