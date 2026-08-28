using TaskGuide.Domain.Common;
using TaskGuide.Domain.Tasks;
using TaskGuide.Domain.Time;
using Xunit;

namespace TaskGuide.Domain.Tests;

/// <summary>
/// `tests/TEST-INVENTORY.md`'s "Recurrence" section.
/// </summary>
public sealed class RecurrenceTests
{
    private static readonly DayBoundary Boundary = new(TimeZoneInfo.FindSystemTimeZoneById(DayBoundary.ZoneId));
    private static readonly TaskId Id = new("t_recurrence");

    /// <summary>Local noon on the given date — unambiguously inside the day, either side of DST.</summary>
    private static DateTimeOffset Noon(string date) => Boundary.EndOf(DateOnly.Parse(date)).AddHours(-12);

    private static CompletionLog Log(params CompletionEntry[] entries) => new(Id, entries);

    [Fact]
    public void Calendar_rules_generate_the_next_deadline_from_the_calendar_ignoring_completions()
    {
        // Bins out every Monday, first due Monday 2026-08-03.
        var recurrence = new Recurrence(
            RecurrenceAnchor.Calendar,
            new EveryNWeeksOn(1, [DayOfWeek.Monday]),
            new DateOnly(2026, 8, 3));
        var createdAt = Noon("2026-08-01");

        // Two completions, one of them wildly off the calendar. Neither moves the deadline.
        var busy = Log(
            new CompletionEntry(new DateOnly(2026, 8, 3), Noon("2026-08-03")),
            new CompletionEntry(new DateOnly(2026, 8, 10), Noon("2026-08-14")));
        var untouched = CompletionLog.Empty(Id);

        var withCompletions = RecurrenceRules.LiveInstanceDeadline(recurrence, createdAt, busy, Noon("2026-08-19"), Boundary);
        var without = RecurrenceRules.LiveInstanceDeadline(recurrence, createdAt, untouched, Noon("2026-08-19"), Boundary);

        Assert.Equal(new DateOnly(2026, 8, 17), withCompletions); // the Monday of that week, from the calendar
        Assert.Equal(withCompletions, without);
    }

    [Fact]
    public void Completion_rules_generate_it_from_last_completed_plus_interval()
    {
        // Descale the kettle every 30 days since it was last done.
        var recurrence = new Recurrence(
            RecurrenceAnchor.Completion,
            new IntervalSinceCompletion(30, OffsetUnit.Days),
            new DateOnly(2026, 6, 1));
        var createdAt = Noon("2026-05-20");
        var log = Log(
            new CompletionEntry(new DateOnly(2026, 6, 1), Noon("2026-06-01")),
            new CompletionEntry(new DateOnly(2026, 7, 1), Noon("2026-07-04")));

        // last(done) = 2026-07-04, + 30 days = 2026-08-03, and that instance is live once it arrives.
        var live = RecurrenceRules.LiveInstanceDeadline(recurrence, createdAt, log, Noon("2026-08-20"), Boundary);

        Assert.Equal(new DateOnly(2026, 8, 3), live);

        // Before it arrives, the instance the last completion satisfied is still the live one —
        // the same derived grace a calendar instance gets, so the Task reads as done meanwhile.
        var duringGrace = RecurrenceRules.LiveInstanceDeadline(recurrence, createdAt, log, Noon("2026-07-20"), Boundary);

        Assert.Equal(new DateOnly(2026, 7, 1), duringGrace);
    }

    [Fact]
    public void A_completion_anchored_Task_never_accrues_a_backlog()
    {
        var recurrence = new Recurrence(
            RecurrenceAnchor.Completion,
            new IntervalSinceCompletion(1, OffsetUnit.Weeks),
            new DateOnly(2026, 1, 5));
        var createdAt = Noon("2026-01-01");
        var neverDone = CompletionLog.Empty(Id);

        // Half a year late, and still the one instance — no pile of missed ones.
        var live = RecurrenceRules.LiveInstanceDeadline(recurrence, createdAt, neverDone, Noon("2026-07-01"), Boundary);
        var missed = RecurrenceRules.ConsecutiveMissedInstances(recurrence, createdAt, neverDone, Noon("2026-07-01"), Boundary);

        Assert.Equal(new DateOnly(2026, 1, 5), live);
        Assert.Equal(0, missed);
    }

    [Fact]
    public void A_completion_anchored_Task_with_no_completions_uses_firstDue_else_CreatedAt()
    {
        var rule = new IntervalSinceCompletion(3, OffsetUnit.Months);
        var createdAt = Noon("2026-02-11");
        var empty = CompletionLog.Empty(Id);

        var withFirstDue = new Recurrence(RecurrenceAnchor.Completion, rule, new DateOnly(2026, 3, 1));
        var withoutFirstDue = new Recurrence(RecurrenceAnchor.Completion, rule, FirstDue: null);

        Assert.Equal(
            new DateOnly(2026, 3, 1),
            RecurrenceRules.LiveInstanceDeadline(withFirstDue, createdAt, empty, Noon("2026-02-20"), Boundary));
        Assert.Equal(
            new DateOnly(2026, 2, 11),
            RecurrenceRules.LiveInstanceDeadline(withoutFirstDue, createdAt, empty, Noon("2026-02-20"), Boundary));
    }

    [Fact]
    public void Exactly_one_instance_is_live_at_a_time()
    {
        // Every 3 days from 2026-03-02: 03-02, 03-05, 03-08, ...
        var recurrence = new Recurrence(RecurrenceAnchor.Calendar, new EveryNDays(3), new DateOnly(2026, 3, 2));
        var createdAt = Noon("2026-03-01");
        var empty = CompletionLog.Empty(Id);

        DateOnly Live(string on) => RecurrenceRules.LiveInstanceDeadline(recurrence, createdAt, empty, Noon(on), Boundary);

        // Before the first deadline the first instance is the live one; nothing earlier exists.
        Assert.Equal(new DateOnly(2026, 3, 2), Live("2026-03-01"));
        Assert.Equal(new DateOnly(2026, 3, 2), Live("2026-03-02"));
        Assert.Equal(new DateOnly(2026, 3, 2), Live("2026-03-04")); // still the 2nd's — not the 2nd *and* the 5th
        Assert.Equal(new DateOnly(2026, 3, 5), Live("2026-03-05"));
        Assert.Equal(new DateOnly(2026, 3, 8), Live("2026-03-08"));
    }

    [Fact]
    public void Grace_equals_one_full_recurrence_period()
    {
        var createdAt = Noon("2026-04-01");
        var empty = CompletionLog.Empty(Id);

        // A weekly instance stays live for a whole week; a monthly one for a whole month.
        var weekly = new Recurrence(RecurrenceAnchor.Calendar, new EveryNWeeksOn(1, [DayOfWeek.Friday]), new DateOnly(2026, 4, 3));
        var monthly = new Recurrence(RecurrenceAnchor.Calendar, new MonthlyOnDayOfMonth(3), new DateOnly(2026, 4, 3));

        // Grace runs exactly to the next instance's deadline — derived from the rule, never a constant.
        Assert.Equal(new DateOnly(2026, 4, 10), RecurrenceRules.NextInstanceDeadlineAfter(weekly, new DateOnly(2026, 4, 3)));
        Assert.Equal(new DateOnly(2026, 5, 3), RecurrenceRules.NextInstanceDeadlineAfter(monthly, new DateOnly(2026, 4, 3)));

        // The day before the successor arrives, the original instance is still the live one.
        Assert.Equal(new DateOnly(2026, 4, 3), RecurrenceRules.LiveInstanceDeadline(weekly, createdAt, empty, Noon("2026-04-09"), Boundary));
        Assert.Equal(new DateOnly(2026, 4, 3), RecurrenceRules.LiveInstanceDeadline(monthly, createdAt, empty, Noon("2026-05-02"), Boundary));

        // The day it arrives, it does not.
        Assert.Equal(new DateOnly(2026, 4, 10), RecurrenceRules.LiveInstanceDeadline(weekly, createdAt, empty, Noon("2026-04-10"), Boundary));
        Assert.Equal(new DateOnly(2026, 5, 3), RecurrenceRules.LiveInstanceDeadline(monthly, createdAt, empty, Noon("2026-05-03"), Boundary));
    }

    [Fact]
    public void A_late_completion_satisfies_the_instance_that_was_live_and_logs_due_done()
    {
        var recurrence = new Recurrence(RecurrenceAnchor.Calendar, new EveryNWeeksOn(1, [DayOfWeek.Monday]), new DateOnly(2026, 8, 3));
        var createdAt = Noon("2026-08-01");
        var empty = CompletionLog.Empty(Id);

        // Done on the Thursday — three days late, still inside the Monday instance's grace.
        var done = Noon("2026-08-06");
        var entry = RecurrenceRules.CompleteLiveInstance(recurrence, createdAt, empty, done, Boundary);

        Assert.Equal(new DateOnly(2026, 8, 3), entry.Due); // the instance that was live, not the day it was done
        Assert.Equal(done, entry.Done);

        var log = empty.With(entry);
        Assert.True(RecurrenceRules.SatisfiesLiveInstance(entry, recurrence, createdAt, log, done, Boundary));

        // Once the next Monday arrives, that same entry no longer satisfies the live instance.
        Assert.False(RecurrenceRules.SatisfiesLiveInstance(entry, recurrence, createdAt, log, Noon("2026-08-10"), Boundary));
    }

    [Fact]
    public void A_missed_instance_is_silently_superseded()
    {
        var recurrence = new Recurrence(RecurrenceAnchor.Calendar, new EveryNWeeksOn(1, [DayOfWeek.Monday]), new DateOnly(2026, 8, 3));
        var createdAt = Noon("2026-08-01");
        var empty = CompletionLog.Empty(Id);

        // Nothing done. On 2026-08-24 the live item is that Monday alone — the 3rd, 10th and 17th
        // are gone, not queued up behind it.
        var live = RecurrenceRules.LiveInstanceDeadline(recurrence, createdAt, empty, Noon("2026-08-24"), Boundary);
        Assert.Equal(new DateOnly(2026, 8, 24), live);

        // They are counted, not reified: three superseded instances, and the live one is not yet missed.
        Assert.Equal(3, RecurrenceRules.ConsecutiveMissedInstances(recurrence, createdAt, empty, Noon("2026-08-24"), Boundary));

        // The count is *consecutive*: a completion on the 10th resets everything before it.
        var partial = Log(new CompletionEntry(new DateOnly(2026, 8, 10), Noon("2026-08-11")));
        Assert.Equal(1, RecurrenceRules.ConsecutiveMissedInstances(recurrence, createdAt, partial, Noon("2026-08-24"), Boundary));

        // And a Task never yet missed has none.
        Assert.Equal(0, RecurrenceRules.ConsecutiveMissedInstances(recurrence, createdAt, empty, Noon("2026-08-03"), Boundary));
    }

    [Fact]
    public void Monthly_on_the_5th_stays_on_the_5th_across_a_year()
    {
        var recurrence = new Recurrence(RecurrenceAnchor.Calendar, new MonthlyOnDayOfMonth(5), new DateOnly(2026, 1, 5));
        var createdAt = Noon("2026-01-01");
        var empty = CompletionLog.Empty(Id);

        for (var month = 1; month <= 12; month++)
        {
            var fifth = new DateOnly(2026, month, 5);
            Assert.Equal(fifth, RecurrenceRules.LiveInstanceDeadline(recurrence, createdAt, empty, Boundary.EndOf(fifth).AddHours(-12), Boundary));
        }

        // Twelve steps of the generator itself, never drifting off the 5th.
        var cursor = new DateOnly(2026, 1, 5);
        for (var month = 2; month <= 12; month++)
        {
            cursor = RecurrenceRules.NextInstanceDeadlineAfter(recurrence, cursor);
            Assert.Equal(new DateOnly(2026, month, 5), cursor);
        }

        Assert.Equal(new DateOnly(2027, 1, 5), RecurrenceRules.NextInstanceDeadlineAfter(recurrence, cursor));

        // The same guarantee at the end of the month, where a fixed number of days would skip
        // February outright: clamping never becomes drift, and the 31st comes back.
        var monthEnd = new Recurrence(RecurrenceAnchor.Calendar, new MonthlyOnDayOfMonth(31), new DateOnly(2026, 1, 31));
        DateOnly[] expected =
        [
            new(2026, 2, 28), new(2026, 3, 31), new(2026, 4, 30), new(2026, 5, 31),
        ];

        var step = new DateOnly(2026, 1, 31);
        foreach (var due in expected)
        {
            step = RecurrenceRules.NextInstanceDeadlineAfter(monthEnd, step);
            Assert.Equal(due, step);
        }
    }

    [Fact]
    public void A_one_off_Tasks_log_holds_at_most_one_entry_and_that_entry_is_what_makes_it_Done()
    {
        var deadline = new DateOnly(2026, 9, 30);
        var log = CompletionLog.Empty(Id);

        Assert.False(log.Covers(deadline));

        log = log.WithOnlyCompletion(new CompletionEntry(deadline, Noon("2026-09-28")));
        Assert.Single(log.Entries);
        Assert.True(log.Covers(deadline)); // the entry is what makes it Done

        // Recording again replaces rather than appends — a one-off has only ever one instance.
        var again = Noon("2026-09-29");
        log = log.WithOnlyCompletion(new CompletionEntry(deadline, again));
        Assert.Single(log.Entries);
        Assert.Equal(again, log.Latest!.Done);

        // An undeadlined one-off works the same way: its only `due` is null.
        var undeadlined = CompletionLog.Empty(Id);
        Assert.False(undeadlined.Covers(null));
        Assert.True(undeadlined.WithOnlyCompletion(new CompletionEntry(null, again)).Covers(null));
    }

    // ---- rules the generator cannot execute -------------------------------

    [Fact]
    public void A_rule_that_never_advances_is_rejected_at_construction()
    {
        // `LiveInstanceDeadline` walks forward one instance at a time until it passes today. A
        // rule whose successor is the instance itself never passes anything: the walk is a
        // non-terminating loop on the tick thread, which is why this is a construction-time
        // rejection rather than something the generator is asked to survive.
        foreach (var rule in new RecurrenceRule[]
                 {
                     new EveryNDays(0),
                     new EveryNDays(-1),
                     new EveryNWeeksOn(0, [DayOfWeek.Monday]),
                     new EveryNWeeksOn(-2, [DayOfWeek.Monday]),
                 })
        {
            var thrown = Assert.Throws<ArgumentException>(
                () => new Recurrence(RecurrenceAnchor.Calendar, rule, null));

            Assert.Contains("at least 1", thrown.Message);
        }

        Assert.Throws<ArgumentException>(
            () => new Recurrence(RecurrenceAnchor.Completion, new IntervalSinceCompletion(0, OffsetUnit.Days), null));
    }

    [Fact]
    public void A_weekly_rule_naming_no_weekday_is_rejected_at_construction()
    {
        var thrown = Assert.Throws<ArgumentException>(() => new Recurrence(
            RecurrenceAnchor.Calendar, new EveryNWeeksOn(1, Array.Empty<DayOfWeek>()), null));

        Assert.Contains("at least one weekday", thrown.Message);
    }

    [Fact]
    public void A_calendar_date_the_rule_can_never_fall_on_is_rejected_at_construction()
    {
        // Day-of-month clamps down to a short month, so the 31st is meaningful; the 0th and the
        // 32nd are not, and neither is a February 30th that could only ever mean the 29th.
        Assert.Throws<ArgumentException>(
            () => new Recurrence(RecurrenceAnchor.Calendar, new MonthlyOnDayOfMonth(0), null));
        Assert.Throws<ArgumentException>(
            () => new Recurrence(RecurrenceAnchor.Calendar, new MonthlyOnDayOfMonth(32), null));
        Assert.Throws<ArgumentException>(
            () => new Recurrence(RecurrenceAnchor.Calendar, new YearlyOn(13, 1), null));
        Assert.Throws<ArgumentException>(
            () => new Recurrence(RecurrenceAnchor.Calendar, new YearlyOn(2, 30), null));

        // The ones that do fall somewhere are left alone.
        _ = new Recurrence(RecurrenceAnchor.Calendar, new MonthlyOnDayOfMonth(31), null);
        _ = new Recurrence(RecurrenceAnchor.Calendar, new YearlyOn(2, 29), null);
    }

    [Fact]
    public void An_anchor_paired_with_a_rule_it_cannot_run_is_rejected_at_construction()
    {
        // Completion-anchored with a calendar rule used to be an unmessaged `InvalidCastException`
        // from inside the generator; calendar-anchored with an interval rule, an
        // `InvalidOperationException` one call deeper. Both are authoring mistakes, so both are
        // named here instead.
        var completionWithCalendarRule = Assert.Throws<ArgumentException>(() => new Recurrence(
            RecurrenceAnchor.Completion, new EveryNDays(3), null));

        Assert.Contains("completion-anchored", completionWithCalendarRule.Message);

        var calendarWithIntervalRule = Assert.Throws<ArgumentException>(() => new Recurrence(
            RecurrenceAnchor.Calendar, new IntervalSinceCompletion(30, OffsetUnit.Days), null));

        Assert.Contains("calendar-anchored", calendarWithIntervalRule.Message);
    }
}
