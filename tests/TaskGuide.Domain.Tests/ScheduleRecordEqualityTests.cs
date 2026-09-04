using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using Xunit;

namespace TaskGuide.Domain.Tests;

/// <summary>
/// #114 sweeps the #115 trap across the Schedule records: a positional record's synthesised
/// <c>Equals</c> compares an <c>IReadOnlyList</c> member by reference, so two structurally
/// identical instances built from freshly-constructed collections would compare unequal.
/// <see cref="DayTemplate"/>'s Windows and EventPrototypes, <see cref="DateOverride"/>'s Windows,
/// and <see cref="DayShape"/>'s Windows and Events are all multisets — a Window is a per-day
/// instance, not a position (`CONTEXT.md` § Availability Window). <see cref="Pattern"/>'s Days is
/// the exception: seven weekday slots indexed positionally by <c>this[DayOfWeek]</c>, so order
/// *is* the meaning. <see cref="PatternBook"/>'s Patterns is a multiset again — <c>Active</c>
/// finds by id, not position.
/// </summary>
public sealed class ScheduleRecordEqualityTests
{
    private static AvailabilityWindow Window(string id, string name) =>
        new(new WindowId(id), name, new TimeOnly(9, 0), new TimeOnly(10, 0), TagSet.Empty);

    private static EventPrototype Prototype(string id, string name) =>
        new(new EventPrototypeId(id), name, new TimeOnly(9, 0), new TimeOnly(10, 0), TagSet.Empty, null);

    private static Event Event(string id, string name) =>
        new(new EventId(id), new DateOnly(2026, 1, 1), name, new TimeOnly(9, 0), new TimeOnly(10, 0), TagSet.Empty, null);

    [Fact]
    public void DayTemplate_Windows_and_EventPrototypes_compare_equal_regardless_of_order()
    {
        var w1 = Window("w_1", "Morning");
        var w2 = Window("w_2", "Evening");
        var p1 = Prototype("ep_1", "Gym");
        var p2 = Prototype("ep_2", "Standup");

        var a = new DayTemplate(new DayTemplateId("dt_1"), "Weekday",
            new[] { w1, w2 }, new[] { p1, p2 });
        var b = new DayTemplate(new DayTemplateId("dt_1"), "Weekday",
            new[] { w2, w1 }, new[] { p2, p1 });

        Assert.NotSame(a.Windows, b.Windows);
        Assert.NotSame(a.EventPrototypes, b.EventPrototypes);
        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void DateOverride_Windows_compares_equal_regardless_of_order()
    {
        var w1 = Window("w_1", "Morning");
        var w2 = Window("w_2", "Evening");

        var a = new DateOverride(new DateOnly(2026, 1, 1), new[] { w1, w2 }, null);
        var b = new DateOverride(new DateOnly(2026, 1, 1), new[] { w2, w1 }, null);

        Assert.NotSame(a.Windows, b.Windows);
        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void DayShape_Windows_and_Events_compare_equal_regardless_of_order()
    {
        var w1 = Window("w_1", "Morning");
        var w2 = Window("w_2", "Evening");
        var e1 = Event("evt_1", "Concert");
        var e2 = Event("evt_2", "Dentist");

        var a = new DayShape(new DateOnly(2026, 1, 1), new[] { w1, w2 }, new[] { e1, e2 }, false);
        var b = new DayShape(new DateOnly(2026, 1, 1), new[] { w2, w1 }, new[] { e2, e1 }, false);

        Assert.NotSame(a.Windows, b.Windows);
        Assert.NotSame(a.Events, b.Events);
        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Pattern_Days_compares_unequal_when_reordered()
    {
        var mon = new DayTemplateId("dt_mon");
        var tue = new DayTemplateId("dt_tue");
        var days = new List<DayTemplateId> { mon, tue, mon, mon, mon, mon, mon };
        var reordered = new List<DayTemplateId> { tue, mon, mon, mon, mon, mon, mon };

        var a = new Pattern(new PatternId("p_1"), "Standard", days);
        var b = new Pattern(new PatternId("p_1"), "Standard", reordered);

        Assert.NotSame(a.Days, b.Days);
        Assert.False(a.Equals(b));
    }

    [Fact]
    public void PatternBook_Patterns_compares_equal_regardless_of_order()
    {
        var days = Enumerable.Repeat(new DayTemplateId("dt_1"), 7).ToList();
        var p1 = new Pattern(new PatternId("p_1"), "Standard", days);
        var p2 = new Pattern(new PatternId("p_2"), "Holiday", days);

        var a = new PatternBook(new PatternId("p_1"), new[] { p1, p2 });
        var b = new PatternBook(new PatternId("p_1"), new[] { p2, p1 });

        Assert.NotSame(a.Patterns, b.Patterns);
        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    /// <summary>
    /// Regression guard against combining <see cref="DayTemplate.Windows"/>'s hash and
    /// <see cref="DayTemplate.EventPrototypes"/>'s hash by plain addition: this exact pair of
    /// templates was found (by a brute-force search over the additively-summed formula) to
    /// collide under that bug despite holding different content — a real counter-example, not
    /// a hoped-for one, since the two member types differ and cannot literally swap an element.
    /// </summary>
    [Fact]
    public void Swapping_elements_between_DayTemplates_two_list_members_compares_unequal_and_hashes_differently()
    {
        var a = new DayTemplate(new DayTemplateId("dt_1"), "Weekday",
            new[] { Window("w_12", "W12"), Window("w_27", "W27") },
            new[] { Prototype("ep_31", "P31"), Prototype("ep_31", "P31"), Prototype("ep_39", "P39") });
        var b = new DayTemplate(new DayTemplateId("dt_1"), "Weekday",
            new[] { Window("w_15", "W15") },
            new[] { Prototype("ep_32", "P32"), Prototype("ep_22", "P22"), Prototype("ep_30", "P30") });

        Assert.NotSame(a.Windows, b.Windows);
        Assert.False(a.Equals(b));
        Assert.NotEqual(a.GetHashCode(), b.GetHashCode());
    }
}
