using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using Xunit;

namespace TaskGuide.Domain.Tests;

/// <summary>
/// #153 review finding 1/5: an Event exception is a separate stored fact the Override never
/// absorbs — applied at read, always, over whatever the date's Events are (the Override's own or
/// the template's), matching `CONTEXT.md` § Event exception's "deleting an instance does not stamp
/// an Override" in the other direction. <see cref="RecurringEvents"/> splits accordingly:
/// <see cref="RecurringEvents.On"/> is the raw prototype materialisation `Freeze` and `Stamp` bake
/// in, and <see cref="RecurringEvents.WithExceptions"/> is what <c>DayShapeReader</c> applies on
/// top of *either* arm at read time. <see cref="DayShapeReaderTests"/> (TaskGuide.Storage.Tests)
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

        var events = RecurringEvents.On(date, [prototype]);

        var actual = Assert.Single(events);
        Assert.Equal("evt_rec_20260831_ep_karate", actual.Id.Value);
    }

    [Fact]
    public void A_deleted_instances_Event_exception_drops_it()
    {
        var date = new DateOnly(2026, 8, 31);
        var prototype = Prototype("ep_karate", "Karate");
        var events = RecurringEvents.On(date, [prototype]);

        var actual = RecurringEvents.WithExceptions(
            date, events, [new EventException(date, prototype.Id, Deleted: true, null, null, null)]);

        Assert.Empty(actual);
    }

    [Fact]
    public void An_edited_instances_Event_exception_replaces_its_name_and_span_leaving_the_prototype_untouched()
    {
        var date = new DateOnly(2026, 8, 31);
        var prototype = Prototype("ep_karate", "Karate", startHour: 17, endHour: 18);
        var events = RecurringEvents.On(date, [prototype]);

        var actual = Assert.Single(RecurringEvents.WithExceptions(
            date, events,
            [new EventException(date, prototype.Id, Deleted: false, "Karate late", new TimeOnly(19, 0), new TimeOnly(20, 0))]));

        Assert.Equal("Karate late", actual.Name);
        Assert.Equal(new TimeOnly(19, 0), actual.Start);
        Assert.Equal(new TimeOnly(20, 0), actual.End);
        Assert.Equal("Karate", prototype.Name);
    }

    /// <summary>
    /// #153 review finding 1: an exception matches by the recurring id it would have minted for
    /// (date, prototypeId), not by (date, prototypeId) against some provenance the Event itself
    /// doesn't carry — so an Event with any other id, such as a promoted or hand-authored one-off,
    /// is left alone even if an exception happens to share the same PrototypeId and date (a
    /// coincidence the fixture makes deliberately implausible-looking to prove the match is by id).
    /// </summary>
    [Fact]
    public void An_exception_matches_by_the_recurring_id_so_a_non_recurring_Event_is_left_untouched()
    {
        var date = new DateOnly(2026, 8, 31);
        var prototypeId = new EventPrototypeId("ep_karate");
        var handAuthored = new Event(new EventId("evt_hand_authored"), date, "Karate makeup", new TimeOnly(9, 0), new TimeOnly(10, 0), TagSet.Empty, null);

        var actual = Assert.Single(RecurringEvents.WithExceptions(
            date, [handAuthored],
            [new EventException(date, prototypeId, Deleted: true, null, null, null)]));

        Assert.Equal(handAuthored, actual);
    }
}
