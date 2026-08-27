namespace TaskGuide.Domain.Tasks;

/// <summary>
/// One Task record holding a rule plus a completion log. The current instance's deadline is
/// computed on demand; nothing dated is ever reified. Recurrence generates the Deadline, so a
/// recurring Task never carries a hand-set one.
/// </summary>
public sealed record Recurrence(RecurrenceAnchor Anchor, RecurrenceRule Rule, DateOnly? FirstDue);

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
public sealed record EveryNWeeksOn(int N, IReadOnlyList<DayOfWeek> Weekdays) : RecurrenceRule;
public sealed record MonthlyOnDayOfMonth(int DayOfMonth) : RecurrenceRule;
public sealed record YearlyOn(int Month, int Day) : RecurrenceRule;

/// <summary>Completion-anchored only: every N days / weeks / months since the last completion.</summary>
public sealed record IntervalSinceCompletion(int N, OffsetUnit Unit) : RecurrenceRule;
