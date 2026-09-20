using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using Xunit;

namespace TaskGuide.Domain.Tests;

/// <summary>
/// #153: the prototype-to-Event materialisation <see cref="DayShapeReader"/> used to own privately
/// is shared with <c>Freeze</c> and <c>Stamp</c>, moved here verbatim. These tests pin the exact
/// behaviour at its new home; <see cref="DayShapeReaderTests"/> (TaskGuide.Storage.Tests) already
/// exercises the same rules through the reader and must not need to change.
/// </summary>
public sealed class RecurringEventsTests
{
    private static EventPrototype Prototype(string id, string name, int startHour = 18, int endHour = 19) =>
        new(new EventPrototypeId(id), name, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0), TagSet.Empty, null);

    [Fact]
    public void A_recurring_instances_Event_id_is_evt_rec_date_prototypeId()
    {
        var date = new DateOnly(2026, 8, 31);
        var prototype = Prototype("ep_karate", "Karate");

        var events = RecurringEvents.On(date, [prototype], []);

        var actual = Assert.Single(events);
        Assert.Equal("evt_rec_20260831_ep_karate", actual.Id.Value);
    }

    [Fact]
    public void A_deleted_instances_Event_exception_drops_it()
    {
        var date = new DateOnly(2026, 8, 31);
        var prototype = Prototype("ep_karate", "Karate");

        var events = RecurringEvents.On(date, [prototype], [new EventException(date, prototype.Id, Deleted: true, null, null, null)]);

        Assert.Empty(events);
    }

    [Fact]
    public void An_edited_instances_Event_exception_replaces_its_name_and_span_leaving_the_prototype_untouched()
    {
        var date = new DateOnly(2026, 8, 31);
        var prototype = Prototype("ep_karate", "Karate", startHour: 17, endHour: 18);

        var events = RecurringEvents.On(
            date, [prototype],
            [new EventException(date, prototype.Id, Deleted: false, "Karate late", new TimeOnly(19, 0), new TimeOnly(20, 0))]);

        var actual = Assert.Single(events);
        Assert.Equal("Karate late", actual.Name);
        Assert.Equal(new TimeOnly(19, 0), actual.Start);
        Assert.Equal(new TimeOnly(20, 0), actual.End);
        Assert.Equal("Karate", prototype.Name);
    }
}
