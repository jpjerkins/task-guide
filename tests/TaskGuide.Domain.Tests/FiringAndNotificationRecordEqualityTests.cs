using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Matching;
using TaskGuide.Domain.Notifications;
using TaskGuide.Domain.Rules;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.Domain.Time;
using Xunit;

namespace TaskGuide.Domain.Tests;

/// <summary>
/// #114 closes the sweep with Firing, Notifications and the evaluation contexts. Two records here
/// carry <b>sequences</b>, not multisets — the exception rather than the rule for this ticket:
/// <see cref="Reminder.Shortlist"/> (ranked) and <see cref="Reminder.Events"/> (date-ascending).
/// <see cref="MatchContext.Fetched"/> is the same shape as <see cref="TagSet.Dimensions"/> — a
/// dictionary of multisets — and follows the same empty-list elision.
/// <see cref="DerivedObligationContext.Shapes"/> is an <see cref="IDayShapeReader"/>, an
/// interface with no value semantics, and deliberately stays a reference comparison.
/// </summary>
public sealed class FiringAndNotificationRecordEqualityTests
{
    private static readonly WindowId W1 = new("w_1");

    private static FireRow Row(string windowId, FireKind kind, DateTimeOffset? firedAt) =>
        new(new WindowId(windowId), kind, "Morning", new TimeOnly(9, 0), new TimeOnly(10, 0), null, firedAt, null, null);

    [Fact]
    public void DayFires_Rows_compares_equal_regardless_of_order()
    {
        var r1 = Row("w_1", FireKind.Window, new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));
        var r2 = Row("w_2", FireKind.Window, null);

        var a = new DayFires(new DateOnly(2026, 1, 1), new[] { r1, r2 });
        var b = new DayFires(new DateOnly(2026, 1, 1), new[] { r2, r1 });

        Assert.NotSame(a.Rows, b.Rows);
        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    private static TaskItem Task(string id, string title) =>
        new(new TaskId(id), title, null, TagSet.Empty, null, null, null, null, DateTimeOffset.UnixEpoch);

    private static EventLine Line(string id, string name, DayOfWeek weekday) =>
        new(new EventId(id), name, weekday);

    private static Reminder MakeReminder(
        IReadOnlyList<TaskItem> shortlist,
        IReadOnlyList<EventLine> events,
        IReadOnlyList<DimensionId> failedFetches) =>
        new(
            "Title",
            "Window",
            shortlist,
            0,
            events,
            new FooterCounts(0, 0, 0),
            failedFetches,
            new Uri("https://jerkins.net/reminder"),
            DateTimeOffset.UnixEpoch);

    [Fact]
    public void Reminder_Shortlist_compares_unequal_when_reordered()
    {
        var t1 = Task("t_1", "First");
        var t2 = Task("t_2", "Second");

        var a = MakeReminder(new[] { t1, t2 }, Array.Empty<EventLine>(), Array.Empty<DimensionId>());
        var b = MakeReminder(new[] { t2, t1 }, Array.Empty<EventLine>(), Array.Empty<DimensionId>());

        Assert.NotSame(a.Shortlist, b.Shortlist);
        Assert.False(a.Equals(b));
    }

    [Fact]
    public void Reminder_Events_compares_unequal_when_reordered()
    {
        var e1 = Line("evt_1", "Concert", DayOfWeek.Monday);
        var e2 = Line("evt_2", "Dentist", DayOfWeek.Tuesday);

        var a = MakeReminder(Array.Empty<TaskItem>(), new[] { e1, e2 }, Array.Empty<DimensionId>());
        var b = MakeReminder(Array.Empty<TaskItem>(), new[] { e2, e1 }, Array.Empty<DimensionId>());

        Assert.NotSame(a.Events, b.Events);
        Assert.False(a.Equals(b));
    }

    [Fact]
    public void Two_separately_constructed_structurally_identical_Reminders_compare_equal()
    {
        var t1 = Task("t_1", "First");
        var t2 = Task("t_2", "Second");
        var e1 = Line("evt_1", "Concert", DayOfWeek.Monday);
        var e2 = Line("evt_2", "Dentist", DayOfWeek.Tuesday);
        var weather = new DimensionId("weather");

        var a = MakeReminder(new[] { t1, t2 }, new[] { e1, e2 }, new[] { weather });
        var b = MakeReminder(new[] { t1, t2 }, new[] { e1, e2 }, new[] { weather });

        Assert.NotSame(a.Shortlist, b.Shortlist);
        Assert.NotSame(a.Events, b.Events);
        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Reminder_FailedFetches_compares_equal_regardless_of_order()
    {
        var weather = new DimensionId("weather");
        var location = new DimensionId("location");

        var a = MakeReminder(Array.Empty<TaskItem>(), Array.Empty<EventLine>(), new[] { weather, location });
        var b = MakeReminder(Array.Empty<TaskItem>(), Array.Empty<EventLine>(), new[] { location, weather });

        Assert.NotSame(a.FailedFetches, b.FailedFetches);
        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    private static AvailabilityWindow Window() =>
        new(W1, "Morning", new TimeOnly(9, 0), new TimeOnly(10, 0), TagSet.Empty);

    [Fact]
    public void MatchContext_Fetched_compares_equal_regardless_of_Dimension_key_insertion_order_and_regardless_of_value_order_within_a_Dimension()
    {
        var weather = new DimensionId("weather");
        var location = new DimensionId("location");
        var sun = new TagValue("sun");
        var rain = new TagValue("rain");

        var a = new MatchContext(
            Window(),
            new TagValue("60m"),
            new Dictionary<DimensionId, IReadOnlyList<TagValue>>
            {
                [weather] = new[] { sun, rain },
                [location] = new[] { new TagValue("home") },
            },
            Array.Empty<DimensionId>());
        var b = new MatchContext(
            Window(),
            new TagValue("60m"),
            new Dictionary<DimensionId, IReadOnlyList<TagValue>>
            {
                [location] = new[] { new TagValue("home") },
                [weather] = new[] { rain, sun },
            },
            Array.Empty<DimensionId>());

        Assert.NotSame(a.Fetched, b.Fetched);
        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void MatchContext_Fetched_a_Dimension_key_mapped_to_an_empty_list_equals_that_key_being_absent()
    {
        var weather = new DimensionId("weather");

        var a = new MatchContext(
            Window(),
            new TagValue("60m"),
            new Dictionary<DimensionId, IReadOnlyList<TagValue>> { [weather] = Array.Empty<TagValue>() },
            Array.Empty<DimensionId>());
        var b = new MatchContext(
            Window(),
            new TagValue("60m"),
            new Dictionary<DimensionId, IReadOnlyList<TagValue>>(),
            Array.Empty<DimensionId>());

        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void MatchContext_FailedFetches_compares_equal_regardless_of_order()
    {
        var weather = new DimensionId("weather");
        var location = new DimensionId("location");

        var a = new MatchContext(
            Window(), new TagValue("60m"),
            new Dictionary<DimensionId, IReadOnlyList<TagValue>>(),
            new[] { weather, location });
        var b = new MatchContext(
            Window(), new TagValue("60m"),
            new Dictionary<DimensionId, IReadOnlyList<TagValue>>(),
            new[] { location, weather });

        Assert.NotSame(a.FailedFetches, b.FailedFetches);
        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    private static readonly DayBoundary Boundary = new(TimeZoneInfo.Utc);

    private static DerivedObligationContext Context(
        IReadOnlyList<Event> datedEvents,
        IReadOnlyList<DateOverride> overrides,
        IReadOnlyList<DerivedCompletionEntry> completions,
        IReadOnlyList<DayTemplate> dayTemplates,
        IDayShapeReader shapes,
        IReadOnlyList<EventException>? eventExceptions = null) =>
        new(DateTimeOffset.UnixEpoch, datedEvents, overrides, shapes, completions, Boundary)
        {
            DayTemplates = dayTemplates,
            EventExceptions = eventExceptions ?? Array.Empty<EventException>(),
        };

    private sealed class StubDayShapeReader : IDayShapeReader
    {
        public DayShape For(DateOnly date) => new(date, Array.Empty<AvailabilityWindow>(), Array.Empty<Event>(), false);
    }

    [Fact]
    public void DerivedObligationContexts_DatedEvents_Overrides_Completions_DayTemplates_and_EventExceptions_each_compare_equal_regardless_of_order()
    {
        var shapes = new StubDayShapeReader();
        var e1 = new Event(new EventId("evt_1"), new DateOnly(2026, 1, 1), "Concert", new TimeOnly(9, 0), new TimeOnly(10, 0), TagSet.Empty, null);
        var e2 = new Event(new EventId("evt_2"), new DateOnly(2026, 1, 2), "Dentist", new TimeOnly(9, 0), new TimeOnly(10, 0), TagSet.Empty, null);
        var o1 = new DateOverride(new DateOnly(2026, 1, 1), Array.Empty<AvailabilityWindow>(), null);
        var o2 = new DateOverride(new DateOnly(2026, 1, 2), Array.Empty<AvailabilityWindow>(), null);
        var c1 = new DerivedCompletionEntry(new RuleId("r_1"), "trigger-1", new DateOnly(2026, 1, 1), DateTimeOffset.UnixEpoch);
        var c2 = new DerivedCompletionEntry(new RuleId("r_2"), "trigger-2", new DateOnly(2026, 1, 2), DateTimeOffset.UnixEpoch);
        var t1 = new DayTemplate(new DayTemplateId("dt_1"), "Weekday", Array.Empty<AvailabilityWindow>(), Array.Empty<EventPrototype>());
        var t2 = new DayTemplate(new DayTemplateId("dt_2"), "Weekend", Array.Empty<AvailabilityWindow>(), Array.Empty<EventPrototype>());
        var x1 = new EventException(new DateOnly(2026, 1, 1), new EventPrototypeId("ep_1"), false, "Moved", new TimeOnly(9, 0), null);
        var x2 = new EventException(new DateOnly(2026, 1, 2), new EventPrototypeId("ep_2"), true, null, null, null);

        var a = Context(new[] { e1, e2 }, new[] { o1, o2 }, new[] { c1, c2 }, new[] { t1, t2 }, shapes, new[] { x1, x2 });
        var b = Context(new[] { e2, e1 }, new[] { o2, o1 }, new[] { c2, c1 }, new[] { t2, t1 }, shapes, new[] { x2, x1 });

        Assert.NotSame(a.DatedEvents, b.DatedEvents);
        Assert.NotSame(a.Overrides, b.Overrides);
        Assert.NotSame(a.Completions, b.Completions);
        Assert.NotSame(a.DayTemplates, b.DayTemplates);
        Assert.NotSame(a.EventExceptions, b.EventExceptions);
        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Two_DerivedObligationContexts_differing_only_in_their_IDayShapeReader_compare_unequal()
    {
        var a = Context(Array.Empty<Event>(), Array.Empty<DateOverride>(), Array.Empty<DerivedCompletionEntry>(), Array.Empty<DayTemplate>(), new StubDayShapeReader());
        var b = Context(Array.Empty<Event>(), Array.Empty<DateOverride>(), Array.Empty<DerivedCompletionEntry>(), Array.Empty<DayTemplate>(), new StubDayShapeReader());

        Assert.False(a.Equals(b));
    }
}
