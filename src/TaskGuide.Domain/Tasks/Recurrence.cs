using TaskGuide.Domain.Common;
using TaskGuide.Domain.Time;

namespace TaskGuide.Domain.Tasks;

/// <summary>
/// One Task record holding a rule plus a completion log. The current instance's deadline is
/// computed on demand; nothing dated is ever reified. Recurrence generates the Deadline, so a
/// recurring Task never carries a hand-set one.
/// </summary>
/// <remarks>
/// <b>A Recurrence the generator cannot execute is rejected here, not discovered there.</b>
/// <see cref="RecurrenceRules.LiveInstanceDeadline"/> walks forward one instance at a time, so a
/// rule whose successor is the instance itself never terminates — on the ~30 s tick loop that is a
/// hung engine, not a bad value. The anchor/rule pairing is checked in the same place, because
/// either mismatch is otherwise found by an exception thrown from inside the walk.
/// <para>
/// <c>Anchor</c> and <c>Rule</c> are get-only for the same reason: the pair is the invariant, so it
/// is not something a <c>with</c> expression may edit one half of.
/// </para>
/// </remarks>
public sealed record Recurrence(RecurrenceAnchor Anchor, RecurrenceRule Rule, DateOnly? FirstDue)
{
    public RecurrenceAnchor Anchor { get; } = Anchor;

    public RecurrenceRule Rule { get; } = Validated(Anchor, Rule);

    private static RecurrenceRule Validated(RecurrenceAnchor anchor, RecurrenceRule rule)
    {
        var isIntervalSinceCompletion = rule is IntervalSinceCompletion;

        if (anchor is RecurrenceAnchor.Completion && !isIntervalSinceCompletion)
        {
            throw new ArgumentException(
                "A completion-anchored Recurrence restarts its clock from the last completion, so its "
                + $"rule must be an {nameof(IntervalSinceCompletion)} — {rule.GetType().Name} is imposed "
                + "by the calendar.",
                nameof(rule));
        }

        if (anchor is RecurrenceAnchor.Calendar && isIntervalSinceCompletion)
        {
            throw new ArgumentException(
                $"A calendar-anchored Recurrence has no completion to count from, so an "
                + $"{nameof(IntervalSinceCompletion)} rule can never produce its next instance.",
                nameof(rule));
        }

        switch (rule)
        {
            case EveryNDays(var n) when n < 1:
                throw Interval(n);

            case EveryNWeeksOn(var n, _) when n < 1:
                throw Interval(n);

            case IntervalSinceCompletion(var n, _) when n < 1:
                throw Interval(n);

            case EveryNWeeksOn(_, var weekdays) when weekdays.Count == 0:
                throw new ArgumentException("A weekly rule must name at least one weekday.", nameof(rule));

            // Day-of-month clamps down to a short month, so the 31st is meaningful; the 0th and
            // the 32nd fall on no month at all.
            case MonthlyOnDayOfMonth(var day) when day is < 1 or > 31:
                throw new ArgumentException(
                    $"A monthly rule's day of month must be between 1 and 31, not {day}.", nameof(rule));

            case YearlyOn(var month, _) when month is < 1 or > 12:
                throw new ArgumentException(
                    $"A yearly rule's month must be between 1 and 12, not {month}.", nameof(rule));

            // February 30th could only ever mean the 29th, so it is an authoring mistake rather
            // than a date to clamp. A leap year is the yardstick: the 29th itself is legitimate.
            case YearlyOn(var month, var day) when day < 1 || day > DateTime.DaysInMonth(2024, month):
                throw new ArgumentException(
                    $"A yearly rule's day must be a day month {month} can have, not {day}.", nameof(rule));

            default:
                return rule;
        }

        static ArgumentException Interval(int n) => new(
            $"A recurrence interval must be at least 1: {n} never reaches a next instance.", nameof(rule));
    }
}

/// <summary>
/// The test: does doing the thing restart the clock, or does the world impose the date?
/// </summary>
public enum RecurrenceAnchor
{
    /// <summary>f(rule, calendar) — bins on collection day, church, the weekly report.</summary>
    Calendar,

    /// <summary>last(completed) + interval — water filter, oil change, descaling. Never accrues a backlog.</summary>
    Completion,
}

/// <summary>
/// A closed set. Full RRULE was rejected (most of the grammar unused, hard to author on a
/// phone, hard to render back as a checkable sentence); interval-only was rejected because
/// months are not a fixed number of days.
/// </summary>
public abstract record RecurrenceRule;

public sealed record EveryNDays(int N) : RecurrenceRule;

public sealed record EveryNWeeksOn(int N, IReadOnlyList<DayOfWeek> Weekdays) : RecurrenceRule
{
    /// <summary><see cref="Weekdays"/> compares as a multiset — a set of weekdays, not a
    /// sequence.</summary>
    public bool Equals(EveryNWeeksOn? other) =>
        other is not null
        && N == other.N
        && StructuralEquality.MultisetEqual(Weekdays, other.Weekdays);

    public override int GetHashCode() =>
        HashCode.Combine(N, StructuralEquality.MultisetHash(Weekdays));
}

public sealed record MonthlyOnDayOfMonth(int DayOfMonth) : RecurrenceRule;
public sealed record YearlyOn(int Month, int Day) : RecurrenceRule;

/// <summary>Completion-anchored only: every N days / weeks / months since the last completion.</summary>
public sealed record IntervalSinceCompletion(int N, OffsetUnit Unit) : RecurrenceRule;

/// <summary>
/// The generator. Nothing dated is ever reified: the live instance's deadline is computed on
/// demand from <c>(rule, anchor, firstDue, createdAt, completionLog, now)</c>, and every member
/// here is a pure function of its arguments.
/// </summary>
/// <remarks>
/// <b>Grace equals one full recurrence period — derived, with no knob.</b> An instance stays
/// live until the next one's deadline arrives, which is why no member takes a grace window.
/// </remarks>
public static class RecurrenceRules
{
    /// <summary>The deadline of the one instance that is live at <paramref name="now"/>.</summary>
    public static DateOnly LiveInstanceDeadline(
        Recurrence recurrence,
        DateTimeOffset createdAt,
        CompletionLog log,
        DateTimeOffset now,
        DayBoundary boundary)
    {
        var today = boundary.DateOf(now);
        var first = FirstInstance(recurrence, createdAt, boundary);

        if (recurrence.Anchor is RecurrenceAnchor.Completion)
        {
            // Never done means no new instance: the start point stands, however late it is.
            if (log.Latest is not { } last)
            {
                return first;
            }

            var successor = AddInterval(boundary.DateOf(last.Done), (IntervalSinceCompletion)recurrence.Rule);

            // Until the successor arrives, the instance the last completion satisfied is still
            // the live one — the same grace a calendar instance gets, derived the same way.
            return today < successor ? last.Due ?? first : successor;
        }

        if (today < first)
        {
            return first;
        }

        var live = first;
        while (true)
        {
            var next = NextInstanceDeadlineAfter(recurrence, live);
            if (next > today)
            {
                return live;
            }

            live = next;
        }
    }

    /// <summary>
    /// The deadline that supersedes <paramref name="instance"/> — the instant grace runs out.
    /// </summary>
    public static DateOnly NextInstanceDeadlineAfter(Recurrence recurrence, DateOnly instance) => recurrence.Rule switch
    {
        EveryNDays(var n) => instance.AddDays(n),
        EveryNWeeksOn rule => NextWeeklyAfter(rule, instance),
        // Stepping by calendar month, never by days: "monthly on the 5th" that walked off the
        // 5th within a year is exactly why interval-only was rejected.
        MonthlyOnDayOfMonth(var day) => OnDayOfMonth(instance.AddMonths(1), day),
        YearlyOn(var month, var day) => InYear(instance.Year + 1, month, day),
        IntervalSinceCompletion => throw new InvalidOperationException(
            "A completion-anchored instance has no successor until it is completed — that is what "
            + "it means for it never to accrue a backlog."),
        _ => throw new ArgumentOutOfRangeException(nameof(recurrence), recurrence.Rule, "Unknown recurrence rule."),
    };

    /// <summary>Whether <paramref name="entry"/> satisfies the instance live at <paramref name="now"/>.</summary>
    public static bool SatisfiesLiveInstance(
        CompletionEntry entry,
        Recurrence recurrence,
        DateTimeOffset createdAt,
        CompletionLog log,
        DateTimeOffset now,
        DayBoundary boundary) =>
        entry.Due == LiveInstanceDeadline(recurrence, createdAt, log, now, boundary);

    /// <summary>
    /// The entry a completion at <paramref name="done"/> writes: the instance that was live,
    /// paired with the instant it was done. A late completion satisfies the live instance.
    /// </summary>
    public static CompletionEntry CompleteLiveInstance(
        Recurrence recurrence,
        DateTimeOffset createdAt,
        CompletionLog log,
        DateTimeOffset done,
        DayBoundary boundary) =>
        new(LiveInstanceDeadline(recurrence, createdAt, log, done, boundary), done);

    /// <summary>
    /// How many instances before the live one were superseded with no completion covering them.
    /// A missed instance is silently superseded; N consecutive misses is what Status reads.
    /// A completion-anchored Task never accrues a backlog, so this is always 0 for one.
    /// </summary>
    public static int ConsecutiveMissedInstances(
        Recurrence recurrence,
        DateTimeOffset createdAt,
        CompletionLog log,
        DateTimeOffset now,
        DayBoundary boundary)
    {
        if (recurrence.Anchor is RecurrenceAnchor.Completion)
        {
            return 0;
        }

        var live = LiveInstanceDeadline(recurrence, createdAt, log, now, boundary);

        // The live instance is still completable, so it is not yet a miss.
        var superseded = new List<DateOnly>();
        for (var d = FirstInstance(recurrence, createdAt, boundary); d < live; d = NextInstanceDeadlineAfter(recurrence, d))
        {
            superseded.Add(d);
        }

        var missed = 0;
        for (var i = superseded.Count - 1; i >= 0 && !log.Covers(superseded[i]); i--)
        {
            missed++;
        }

        return missed;
    }

    /// <summary>
    /// The start point: an explicit first-due date, else <c>CreatedAt</c>, moved forward to the
    /// first date the rule can actually fall on.
    /// </summary>
    private static DateOnly FirstInstance(Recurrence recurrence, DateTimeOffset createdAt, DayBoundary boundary)
    {
        var start = recurrence.FirstDue ?? boundary.DateOf(createdAt);

        return recurrence.Rule switch
        {
            EveryNWeeksOn rule => FirstWeekdayOnOrAfter(rule, start),
            MonthlyOnDayOfMonth(var day) => AtLeast(OnDayOfMonth(start, day), start, next => OnDayOfMonth(next.AddMonths(1), day)),
            YearlyOn(var month, var day) => AtLeast(InYear(start.Year, month, day), start, _ => InYear(start.Year + 1, month, day)),
            _ => start,
        };
    }

    private static DateOnly AtLeast(DateOnly candidate, DateOnly floor, Func<DateOnly, DateOnly> advance) =>
        candidate >= floor ? candidate : advance(candidate);

    private static DateOnly FirstWeekdayOnOrAfter(EveryNWeeksOn rule, DateOnly start)
    {
        var candidate = start;
        for (var i = 0; i < 7; i++, candidate = candidate.AddDays(1))
        {
            if (rule.Weekdays.Contains(candidate.DayOfWeek))
            {
                return candidate;
            }
        }

        throw new ArgumentException("A weekly rule must name at least one weekday.", nameof(rule));
    }

    /// <summary>
    /// Weeks are counted in blocks from the Sunday of the week the instance falls in, so a
    /// multi-weekday rule emits every named day before the next block begins.
    /// </summary>
    private static DateOnly NextWeeklyAfter(EveryNWeeksOn rule, DateOnly instance)
    {
        var blockStart = instance.AddDays(-(int)instance.DayOfWeek);

        var withinBlock = rule.Weekdays
            .Select(day => blockStart.AddDays((int)day))
            .Where(candidate => candidate > instance)
            .Order()
            .ToList();

        if (withinBlock.Count > 0)
        {
            return withinBlock[0];
        }

        var nextBlock = blockStart.AddDays(7 * rule.N);
        return rule.Weekdays.Select(day => nextBlock.AddDays((int)day)).Min();
    }

    /// <summary>Clamps to the last day of the month — the 31st of a 30-day month is its 30th.</summary>
    private static DateOnly OnDayOfMonth(DateOnly within, int dayOfMonth) =>
        new(within.Year, within.Month, Math.Min(dayOfMonth, DateTime.DaysInMonth(within.Year, within.Month)));

    private static DateOnly InYear(int year, int month, int day) =>
        new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));

    private static DateOnly AddInterval(DateOnly from, IntervalSinceCompletion rule) => rule.Unit switch
    {
        OffsetUnit.Days => from.AddDays(rule.N),
        OffsetUnit.Weeks => from.AddDays(rule.N * 7),
        OffsetUnit.Months => from.AddMonths(rule.N),
        _ => throw new ArgumentOutOfRangeException(nameof(rule), rule.Unit, "Unknown offset unit."),
    };
}
