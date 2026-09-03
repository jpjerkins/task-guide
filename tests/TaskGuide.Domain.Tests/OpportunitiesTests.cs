using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Ranking;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.Domain.Time;
using Xunit;

namespace TaskGuide.Domain.Tests;

/// <summary>
/// `tests/TEST-INVENTORY.md`'s "Opportunities and the horizon" section.
/// </summary>
public sealed class OpportunitiesTests
{
    private static readonly DayBoundary Boundary = new(TimeZoneInfo.FindSystemTimeZoneById(DayBoundary.ZoneId));
    private static readonly ClockTimeResolution Resolution = new(Boundary);
    private static readonly DimensionRegistry Registry = KnownDimensions.Default;

    /// <summary>Tuesday.</summary>
    private static readonly DateOnly Tuesday = new(2026, 9, 1);

    /// <summary>Wednesday, the day after <see cref="Tuesday"/>.</summary>
    private static readonly DateOnly Wednesday = new(2026, 9, 2);

    /// <summary>Half an hour of work — admitted by an hour-long Window, not by a ten-minute one.</summary>
    private static TaskItem HalfHourTask(DateOnly? deadline = null, DateOnly? postpone = null) => new(
        new TaskId("t_opportunities"),
        "Do the thing",
        Notes: null,
        Tags: new TagSet(
            new Dictionary<DimensionId, IReadOnlyList<TagValue>>
            {
                [KnownDimensions.Duration] = [new TagValue("30")],
            },
            Array.Empty<LooseTag>()),
        Deadline: deadline,
        Defer: null,
        Postpone: postpone,
        Recurrence: null,
        CreatedAt: Resolution.Resolve(new DateOnly(2026, 8, 1), new TimeOnly(9, 0)));

    /// <summary>The same Task, plus a Tag on the one axis whose Window-side value is fetched.</summary>
    private static TaskItem SunnyHalfHourTask()
    {
        var plain = HalfHourTask();

        return plain with
        {
            Tags = new TagSet(
                new Dictionary<DimensionId, IReadOnlyList<TagValue>>(plain.Tags.Dimensions)
                {
                    [KnownDimensions.Weather] = [new TagValue("sunny")],
                },
                Array.Empty<LooseTag>()),
        };
    }

    private static AvailabilityWindow Window(string id, int startHour, int endHour, int endMinute = 0) =>
        new(new WindowId(id), "Some window", new TimeOnly(startHour, 0), new TimeOnly(endHour, endMinute), TagSet.Empty);

    private static DateTimeOffset At(DateOnly date, int hour) => Resolution.Resolve(date, new TimeOnly(hour, 0));

    private static OpportunityCounter CounterOver(IDayShapeReader shapes) =>
        new(shapes, Registry, Resolution, Boundary);

    /// <summary>The only view of the calendar: a shape per date, and reading one never writes one.</summary>
    private sealed class FakeShapes(Func<DateOnly, DayShape> shapeOf) : IDayShapeReader
    {
        public List<DateOnly> DatesRead { get; } = [];

        public DayShape For(DateOnly date)
        {
            DatesRead.Add(date);
            return shapeOf(date);
        }
    }

    private static DayShape Empty(DateOnly date) => new(date, [], [], IsOverridden: false);

    private static FakeShapes EveryDay(params AvailabilityWindow[] windows) =>
        new(date => new DayShape(date, windows, [], IsOverridden: false));

    private static FakeShapes OnWeekday(DayOfWeek weekday, params AvailabilityWindow[] windows) =>
        new(date => date.DayOfWeek == weekday
            ? new DayShape(date, windows, [], IsOverridden: false)
            : Empty(date));

    [Fact]
    public void Without_a_Deadline_the_horizon_is_a_true_rolling_7_x_24h()
    {
        var shapes = EveryDay(Window("w_morning", 9, 10));
        var now = At(Tuesday, 12);

        // 09:00 on each of the seven dates after now, up to and including the morning of the
        // eighth date — that last one still falls inside 7 x 24h of a midday "now". A horizon
        // snapped to date boundaries would stop a day short and report six.
        Assert.Equal(7, CounterOver(shapes).CountAhead(HalfHourTask(), now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(12)]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    [InlineData(23)]
    public void A_once_a_week_opportunity_counts_exactly_once_at_any_hour_outside_it(int hour)
    {
        var shapes = OnWeekday(DayOfWeek.Wednesday, Window("w_evening", 18, 19));

        // Asked on the very weekday the Window falls on, a horizon that snapped to whole dates
        // would see both this Wednesday's Window and next Wednesday's for every hour before 18.
        Assert.Equal(1, CounterOver(shapes).CountAhead(HalfHourTask(), At(Wednesday, hour)));
    }

    [Fact]
    public void With_a_Deadline_ahead_the_horizon_runs_to_the_end_of_that_day()
    {
        var shapes = EveryDay(Window("w_morning", 9, 10), Window("w_night", 22, 23));
        var now = At(Tuesday, 12);

        // Tonight's Window and two Windows on each of the two days before it is due. The night
        // Window on the Deadline day is the one that matters: it falls after this midday "now",
        // so a horizon running to the same clock time on the Deadline day would score 4 and
        // silently drop it.
        Assert.Equal(5, CounterOver(shapes).CountAhead(HalfHourTask(deadline: Tuesday.AddDays(2)), now));
    }

    [Fact]
    public void A_Window_you_are_standing_in_still_counts_as_an_Opportunity()
    {
        var shapes = OnWeekday(DayOfWeek.Wednesday, Window("w_evening", 18, 19));
        var halfwayThrough = Resolution.Resolve(Wednesday, new TimeOnly(18, 30));

        // Half an hour in, with half an hour left — a chance you can still take, which is what
        // `SnoozePolicy.CeilingFor` already asserts in code by re-deriving the ceiling from the
        // time *actually remaining*. And a notification's landing page is read *inside* a running
        // Window by construction, so a count that excluded it was off by one exactly when read.
        Assert.Equal(2, CounterOver(shapes).CountAhead(HalfHourTask(), halfwayThrough));
    }

    [Fact]
    public void The_far_edge_of_the_horizon_is_unchanged_a_Window_starting_at_the_horizon_end_never_counts()
    {
        var shapes = EveryDay(Window("w_midday", 12, 13));
        var now = At(Tuesday, 12);

        // Only the near edge moved. Today's Window counts (it is running, and it also starts
        // exactly at now); the one a rolling seven days later starts exactly *at* the horizon end
        // and so does not — half-open there still, or a once-a-week chance would count twice.
        Assert.Equal(7, CounterOver(shapes).CountAhead(HalfHourTask(), now));
    }

    [Fact]
    public void With_a_Deadline_passed_the_bound_is_dropped_and_the_horizon_reverts_to_a_rolling_7_days()
    {
        var shapes = EveryDay(Window("w_morning", 9, 10));
        var now = At(Tuesday, 12);

        var overdue = CounterOver(shapes).CountAhead(HalfHourTask(deadline: Tuesday.AddDays(-3)), now);

        // Exactly what the same Task would score with no Deadline at all: the negative horizon
        // is not clamped to zero, it is not applied.
        Assert.Equal(CounterOver(EveryDay(Window("w_morning", 9, 10))).CountAhead(HalfHourTask(), now), overdue);
        Assert.Equal(7, overdue);
    }

    [Fact]
    public void An_overdue_Task_therefore_never_misreports_as_an_Orphan()
    {
        var template = new DayTemplate(new DayTemplateId("dt_any"), "Any day", [Window("w_morning", 9, 10)], []);
        var pattern = new Pattern(new PatternId("p_normal"), "Normal", [.. Enumerable.Repeat(template.Id, 7)]);
        var counter = CounterOver(EveryDay(Window("w_morning", 9, 10)));
        var overdue = HalfHourTask(deadline: Tuesday.AddDays(-30));

        var patternWeekCount = counter.CountInPatternWeek(overdue, pattern, [template], Tuesday);

        Assert.True(counter.CountAhead(overdue, At(Tuesday, 12)) > 0);
        Assert.False(OrphanDetection.IsTaskOrphan(Status.Active, patternWeekCount));
    }

    [Fact]
    public void The_count_walks_real_dates_so_an_Override_removing_the_only_admitting_Window_drops_it()
    {
        var thursday = Tuesday.AddDays(2);
        var evening = Window("w_evening", 18, 19);

        var untouched = OnWeekday(DayOfWeek.Thursday, evening);
        Assert.Equal(1, CounterOver(untouched).CountAhead(HalfHourTask(), At(Tuesday, 12)));

        var travelDay = new FakeShapes(date => date == thursday
            ? new DayShape(date, [], [], IsOverridden: true)
            : untouched.For(date));

        Assert.Equal(0, CounterOver(travelDay).CountAhead(HalfHourTask(), At(Tuesday, 12)));
    }

    [Fact]
    public void A_dated_Event_displacing_a_Window_drops_it()
    {
        var thursday = Tuesday.AddDays(2);
        var evening = Window("w_evening", 18, 19);
        var concert = new Event(
            new EventId("evt_concert"), thursday, "Concert",
            new TimeOnly(18, 10), new TimeOnly(22, 0), TagSet.Empty, AbsenceNotice: null);

        // The Event took all but the first ten minutes of the Window, so the shape carries the
        // truncated remainder — which no longer promises the half hour this Task needs.
        var displaced = new FakeShapes(date => date == thursday
            ? new DayShape(date, [Window("w_evening", 18, 18, endMinute: 10)], [concert], IsOverridden: true)
            : date.DayOfWeek == DayOfWeek.Thursday
                ? new DayShape(date, [evening], [], IsOverridden: false)
                : Empty(date));

        Assert.Equal(1, CounterOver(OnWeekday(DayOfWeek.Thursday, evening)).CountAhead(HalfHourTask(), At(Tuesday, 12)));
        Assert.Equal(0, CounterOver(displaced).CountAhead(HalfHourTask(), At(Tuesday, 12)));
    }

    [Fact]
    public void Switching_the_active_Pattern_moves_the_count()
    {
        var workday = new DayTemplate(new DayTemplateId("dt_workday"), "Workday", [Window("w_evening", 18, 19)], []);
        var restday = new DayTemplate(new DayTemplateId("dt_rest"), "Rest day", [], []);
        var weekendday = new DayTemplate(new DayTemplateId("dt_weekend"), "Weekend day", [Window("w_afternoon", 13, 15)], []);

        // Weekdays only, versus weekends only.
        var busyWeek = Days(d => d is DayOfWeek.Saturday or DayOfWeek.Sunday ? restday : workday);
        var quietWeek = Days(d => d is DayOfWeek.Saturday or DayOfWeek.Sunday ? weekendday : restday);

        var task = HalfHourTask();
        var now = At(Tuesday, 12);

        Assert.Equal(5, CounterOver(ShapesOf(busyWeek)).CountAhead(task, now));
        Assert.Equal(2, CounterOver(ShapesOf(quietWeek)).CountAhead(task, now));

        Pattern Days(Func<DayOfWeek, DayTemplate> pick) => new(
            new PatternId("p_x"), "Some pattern",
            [.. Enum.GetValues<DayOfWeek>().Select(d => pick(d).Id)]);

        FakeShapes ShapesOf(Pattern pattern) => new(date =>
        {
            var template = new[] { workday, restday, weekendday }.Single(t => t.Id == pattern[date.DayOfWeek]);
            return new DayShape(date, template.Windows, [], IsOverridden: false);
        });
    }

    [Fact]
    public void The_Pattern_week_count_ignores_Overrides_and_Events()
    {
        var workday = new DayTemplate(new DayTemplateId("dt_workday"), "Workday", [Window("w_evening", 18, 19)], []);
        var restday = new DayTemplate(new DayTemplateId("dt_rest"), "Rest day", [], []);
        var pattern = new Pattern(
            new PatternId("p_normal"), "Normal",
            [.. Enum.GetValues<DayOfWeek>().Select(d => d == DayOfWeek.Thursday ? workday.Id : restday.Id)]);

        // Every real date is overridden away, so there is no Opportunity ahead at all — but the
        // active Pattern still declares a Window that would admit this Task.
        var counter = CounterOver(new FakeShapes(date => new DayShape(date, [], [], IsOverridden: true)));

        Assert.Equal(0, counter.CountAhead(HalfHourTask(), At(Tuesday, 12)));
        Assert.Equal(1, counter.CountInPatternWeek(HalfHourTask(), pattern, [workday, restday], Tuesday));
    }

    [Fact]
    public void A_fetched_axis_constrains_nothing_in_the_Pattern_week_count_so_a_weather_tagged_Task_is_not_an_Orphan()
    {
        var workday = new DayTemplate(new DayTemplateId("dt_workday"), "Workday", [Window("w_evening", 18, 19)], []);
        var pattern = new Pattern(new PatternId("p_normal"), "Normal", [.. Enumerable.Repeat(workday.Id, 7)]);
        var counter = CounterOver(EveryDay(Window("w_evening", 18, 19)));
        var sunny = SunnyHalfHourTask();

        // Could any Window in the active Pattern *ever* admit this Task? The weather is not a
        // constraint on that question — no Window declares a Weather value and none ever could,
        // so failing it closed here would badge a perfectly well-formed Task as an Orphan and
        // send the user to declare a Tag on a Dimension whose window side is blank by design.
        var patternWeekCount = counter.CountInPatternWeek(sunny, pattern, [workday], Tuesday);

        Assert.Equal(7, patternWeekCount);
        Assert.False(OrphanDetection.IsTaskOrphan(Status.Active, patternWeekCount));
    }

    [Fact]
    public void CountAhead_still_fails_closed_on_a_fetched_axis_it_cannot_know_for_a_future_Window()
    {
        var counter = CounterOver(EveryDay(Window("w_evening", 18, 19)));
        var now = At(Tuesday, 12);

        // The same seven Windows the untagged Task counts. Nothing has been fetched for a Window
        // three days out, and unknown resolves to the empty set — so this one counts none.
        Assert.Equal(7, counter.CountAhead(HalfHourTask(), now));
        Assert.Equal(0, counter.CountAhead(SunnyHalfHourTask(), now));
    }

    [Fact]
    public void The_Pattern_week_count_is_defined_for_a_Task_that_is_not_currently_eligible()
    {
        var workday = new DayTemplate(new DayTemplateId("dt_workday"), "Workday", [Window("w_evening", 18, 19)], []);
        var pattern = new Pattern(new PatternId("p_normal"), "Normal", [.. Enumerable.Repeat(workday.Id, 7)]);
        var counter = CounterOver(EveryDay(Window("w_evening", 18, 19)));

        // Postponed into next month: no Opportunities to speak of, yet the Pattern-week count is
        // still a number — which is what keeps its absence from reading as a zero.
        var postponed = HalfHourTask(postpone: Tuesday.AddDays(40));

        Assert.Equal(7, counter.CountInPatternWeek(postponed, pattern, [workday], Tuesday));
    }
}
