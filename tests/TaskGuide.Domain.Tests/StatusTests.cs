using System.Reflection;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.Domain.Time;
using Xunit;

namespace TaskGuide.Domain.Tests;

/// <summary>
/// `tests/TEST-INVENTORY.md`'s "Status — derived, ordered, first-match-wins" and
/// "Eligibility and the two clocks" sections.
/// </summary>
public sealed class StatusTests
{
    private static readonly DayBoundary Boundary = new(TimeZoneInfo.FindSystemTimeZoneById(DayBoundary.ZoneId));
    private static readonly DimensionRegistry Registry = KnownDimensions.Default;
    private static readonly TaskId Id = new("t_status");

    /// <summary>
    /// Thresholds arrive as a parameter, never as a constant. 30 days is chosen so that the
    /// 59-day ordering case below genuinely computes `Stale` as well as `Unprocessed`.
    /// </summary>
    private static readonly StaleThresholds Thresholds = new(TimeSpan.FromDays(30), ConsecutiveMissedInstances: 3);

    /// <summary>Local noon on the given date — unambiguously inside the day, either side of DST.</summary>
    private static DateTimeOffset Noon(string date) => Boundary.EndOf(DateOnly.Parse(date)).AddHours(-12);

    /// <summary>The instant a date begins locally — what `now >= Defer` and the age clock compare against.</summary>
    private static DateTimeOffset StartOf(string date) => Boundary.EndOf(DateOnly.Parse(date).AddDays(-1));

    private static DateOnly Date(string date) => DateOnly.Parse(date);

    private static CompletionLog Log(params CompletionEntry[] entries) => new(Id, entries);

    private static TagSet Duration(string bucket) => new(
        new Dictionary<DimensionId, IReadOnlyList<TagValue>> { [KnownDimensions.Duration] = [new TagValue(bucket)] },
        Array.Empty<LooseTag>());

    private static TaskItem Item(
        DateTimeOffset createdAt,
        TagSet? tags = null,
        DateOnly? deadline = null,
        Defer? defer = null,
        DateOnly? postpone = null,
        Recurrence? recurrence = null,
        DerivedProvenance? provenance = null) =>
        new(Id, "Sweep the garage", null, tags ?? Duration("30"), deadline, defer, postpone, recurrence, createdAt)
        {
            Provenance = provenance,
        };

    /// <summary>Bins out every Monday, first due Monday 2026-08-03.</summary>
    private static Recurrence Weekly() =>
        new(RecurrenceAnchor.Calendar, new EveryNWeeksOn(1, [DayOfWeek.Monday]), Date("2026-08-03"));

    private static Status Of(TaskItem task, CompletionLog log, DateTimeOffset now) =>
        StatusRules.Of(task, log, Registry, Thresholds, now, Boundary);

    private static bool Eligible(TaskItem task, CompletionLog log, DateTimeOffset now) =>
        StatusRules.IsEligible(task, log, Registry, Thresholds, now, Boundary);

    // ── Status ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_task_with_a_completion_entry_covering_the_current_instance_reads_done()
    {
        var oneOff = Item(Noon("2026-08-01"), deadline: Date("2026-08-20"));
        var recurring = Item(Noon("2026-08-01"), recurrence: Weekly());
        var now = Noon("2026-08-27");

        // The one-off's instance is its Deadline; the recurring Task's is the live 2026-08-24.
        Assert.Equal(Status.Done, Of(oneOff, Log(new CompletionEntry(Date("2026-08-20"), Noon("2026-08-21"))), now));
        Assert.Equal(Status.Done, Of(recurring, Log(new CompletionEntry(Date("2026-08-24"), Noon("2026-08-25"))), now));

        // A completion covering some *other* instance does not.
        Assert.NotEqual(Status.Done, Of(recurring, Log(new CompletionEntry(Date("2026-08-17"), Noon("2026-08-17"))), now));
    }

    [Fact]
    public void A_task_with_no_duration_reads_unprocessed()
    {
        var task = Item(Noon("2026-08-25"), tags: TagSet.Empty);

        Assert.Equal(Status.Unprocessed, Of(task, CompletionLog.Empty(Id), Noon("2026-08-27")));
    }

    [Fact]
    public void A_task_with_no_duration_that_is_also_59_days_old_reads_unprocessed_not_stale()
    {
        var now = Noon("2026-08-27");
        var createdAt = now.AddDays(-59);
        var undeadlined = Item(createdAt, tags: TagSet.Empty);

        Assert.Equal(Status.Unprocessed, Of(undeadlined, CompletionLog.Empty(Id), now));

        // The same Task with a Duration *does* stale, so `Stale` genuinely co-occurred above
        // and the order is what decided it.
        Assert.Equal(Status.Stale, Of(undeadlined with { Tags = Duration("30") }, CompletionLog.Empty(Id), now));
    }

    [Fact]
    public void A_task_with_no_duration_that_is_also_completed_reads_done()
    {
        var task = Item(Noon("2026-08-01"), tags: TagSet.Empty);
        var log = Log(new CompletionEntry(null, Noon("2026-08-26")));

        Assert.Equal(Status.Done, Of(task, log, Noon("2026-08-27")));
    }

    [Fact]
    public void An_undeadlined_one_off_task_aged_past_the_threshold_reads_stale()
    {
        var now = Noon("2026-08-27");
        var task = Item(now.AddDays(-40));

        Assert.Equal(Status.Stale, Of(task, CompletionLog.Empty(Id), now));

        // One day short of the threshold it is not yet stale.
        Assert.Equal(Status.Active, Of(Item(now.AddDays(-29)), CompletionLog.Empty(Id), now));
    }

    [Fact]
    public void A_one_off_task_with_a_deadline_is_never_staled_by_age()
    {
        var now = Noon("2026-08-27");
        var ancient = Item(Noon("2016-01-04"), deadline: Date("2027-01-01"));

        Assert.Equal(Status.Active, Of(ancient, CompletionLog.Empty(Id), now));
    }

    [Fact]
    public void A_recurring_task_with_n_consecutive_missed_instances_reads_stale()
    {
        // Mondays 2026-08-03, 08-10, 08-17 are superseded and uncompleted; 08-24 is live.
        var task = Item(Noon("2026-08-01"), recurrence: Weekly());

        Assert.Equal(Status.Stale, Of(task, CompletionLog.Empty(Id), Noon("2026-08-27")));
    }

    [Fact]
    public void A_recurring_task_with_n_minus_1_consecutive_misses_and_one_completion_between_them_reads_active()
    {
        // Live instance is 2026-09-07; 08-24 and 08-31 are missed, and the 08-17 completion
        // breaks the streak before the older misses can be counted.
        var task = Item(Noon("2026-08-01"), recurrence: Weekly());
        var log = Log(new CompletionEntry(Date("2026-08-17"), Noon("2026-08-17")));

        Assert.Equal(Status.Active, Of(task, log, Noon("2026-09-07")));
    }

    [Fact]
    public void A_task_past_its_deadline_reads_active()
    {
        // 57 days old, well past the 30-day threshold, and past its Deadline: still Active.
        var task = Item(Noon("2026-07-01"), deadline: Date("2026-08-01"));

        Assert.Equal(Status.Active, Of(task, CompletionLog.Empty(Id), Noon("2026-08-27")));
    }

    [Fact]
    public void Nothing_in_the_model_can_write_a_status()
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var domain = typeof(StatusRules).Assembly.GetTypes().Where(t => !t.IsEnum).ToList();

        // No field anywhere holds one — which also catches a record property's backing field,
        // so nothing in the persisted shape carries a Status.
        var statusFields = domain
            .SelectMany(t => t.GetFields(All).Where(f => f.FieldType == typeof(Status) || f.FieldType == typeof(Status?))
                .Select(f => $"{t.FullName}.{f.Name}"))
            .ToList();
        Assert.Empty(statusFields);

        // And no property exposes a setter (or an init accessor) for one.
        var writable = domain
            .SelectMany(t => t.GetProperties(All)
                .Where(p => (p.PropertyType == typeof(Status) || p.PropertyType == typeof(Status?)) && p.SetMethod is not null)
                .Select(p => $"{t.FullName}.{p.Name}"))
            .ToList();
        Assert.Empty(writable);

        // The one way to obtain one is to derive it.
        Assert.Equal(typeof(Status), typeof(StatusRules).GetMethod(nameof(StatusRules.Of))!.ReturnType);
        Assert.DoesNotContain(typeof(TaskItem).GetMembers(All), m => m.Name.Contains("Status", StringComparison.Ordinal));
    }

    // ── Eligibility and the two clocks ────────────────────────────────────────────────

    [Fact]
    public void Eligible_is_active_and_now_at_or_after_defer_and_now_at_or_after_postpone()
    {
        var now = Noon("2026-08-27");
        var empty = CompletionLog.Empty(Id);
        var createdAt = Noon("2026-08-20");

        Assert.True(Eligible(Item(createdAt), empty, now));

        // Each term, falsified on its own.
        Assert.False(Eligible(Item(createdAt, tags: TagSet.Empty), empty, now));                              // not Active
        Assert.False(Eligible(Item(createdAt, defer: new AbsoluteDefer(Date("2026-09-15"))), empty, now));    // now < Defer
        Assert.False(Eligible(Item(createdAt, postpone: Date("2026-09-15")), empty, now));                    // now < Postpone

        // Both gates are `>=`: the instant the date begins, the Task surfaces.
        var deferred = Item(createdAt, defer: new AbsoluteDefer(Date("2026-08-27")), postpone: Date("2026-08-27"));
        Assert.True(Eligible(deferred, empty, StartOf("2026-08-27")));
        Assert.False(Eligible(deferred, empty, StartOf("2026-08-27").AddTicks(-1)));
    }

    [Fact]
    public void A_deferred_task_is_absent_from_every_match_driven_surface_but_present_in_the_task_list()
    {
        var task = Item(Noon("2026-08-20"), defer: new AbsoluteDefer(Date("2026-09-15")));
        var now = Noon("2026-08-27");

        Assert.False(Eligible(task, CompletionLog.Empty(Id), now));

        // Nothing is wrong with it: it reads `Active` and carries its surface date, so the task
        // list — which is not match-driven — still shows it.
        Assert.Equal(Status.Active, Of(task, CompletionLog.Empty(Id), now));
        Assert.Equal(Date("2026-09-15"), DeferRules.ResolvedFor(task, CompletionLog.Empty(Id), now, Boundary));
    }

    [Fact]
    public void Age_is_now_minus_max_of_created_at_and_defer()
    {
        var now = Noon("2026-08-27");
        var empty = CompletionLog.Empty(Id);

        // Defer is the later of the two, so it is what the clock runs from.
        var paused = Item(Noon("2026-07-01"), defer: new AbsoluteDefer(Date("2026-08-10")));
        Assert.Equal(now - StartOf("2026-08-10"), StatusRules.AgeOf(paused, empty, now, Boundary));

        // A Defer already elapsed when the Task was created changes nothing.
        var irrelevant = Item(Noon("2026-07-01"), defer: new AbsoluteDefer(Date("2026-06-01")));
        Assert.Equal(now - Noon("2026-07-01"), StatusRules.AgeOf(irrelevant, empty, now, Boundary));

        // And the pause is what keeps it off the stale pile: 57 days old, 17 days aged.
        Assert.Equal(Status.Active, Of(paused, empty, now));
        Assert.Equal(Status.Stale, Of(irrelevant, empty, now));
    }

    [Fact]
    public void Postpone_does_not_pause_the_age_clock()
    {
        var now = Noon("2026-08-27");
        var createdAt = now.AddDays(-40);
        var pushedAway = Item(createdAt, postpone: Date("2026-09-15"));
        var empty = CompletionLog.Empty(Id);

        Assert.Equal(now - createdAt, StatusRules.AgeOf(pushedAway, empty, now, Boundary));

        // A Task pushed away repeatedly still stales on schedule.
        Assert.Equal(Status.Stale, Of(pushedAway, empty, now));
    }

    [Fact]
    public void A_postponed_task_cannot_also_be_deferred_in_the_future()
    {
        var now = Noon("2026-08-27");
        var empty = CompletionLog.Empty(Id);

        // The gesture only reaches Tasks a match-driven surface showed, and a Task deferred into
        // the future is on none of them.
        Assert.False(Eligible(Item(Noon("2026-08-01"), defer: new AbsoluteDefer(Date("2026-09-15"))), empty, now));

        // So every postponable Task has an elapsed Defer, and `max` needs no third term: the
        // Postpone below is in the future and the age still runs from the elapsed Defer.
        var reachable = Item(Noon("2026-07-01"), defer: new AbsoluteDefer(Date("2026-08-10")), postpone: Date("2026-09-15"));
        Assert.Equal(now - StartOf("2026-08-10"), StatusRules.AgeOf(reachable, empty, now, Boundary));
    }

    [Fact]
    public void An_offset_defer_on_a_recurring_task_resolves_against_the_generated_deadline_per_instance()
    {
        // Surface the day before each Monday instance.
        var task = Item(Noon("2026-08-01"), recurrence: Weekly(), defer: new OffsetDefer(new BeforeOffset(1, OffsetUnit.Days)));
        var empty = CompletionLog.Empty(Id);

        // Live instance 2026-08-24 → the Sunday before it.
        Assert.Equal(Date("2026-08-23"), DeferRules.ResolvedFor(task, empty, Noon("2026-08-27"), Boundary));

        // Per instance: a week later the live instance has moved, and so has the Defer.
        Assert.Equal(Date("2026-08-30"), DeferRules.ResolvedFor(task, empty, Noon("2026-09-03"), Boundary));

        // And it gates: the first instance is due 08-03, so the Task surfaces on 08-02 and not
        // on 08-01.
        Assert.False(Eligible(task, empty, Noon("2026-08-01")));
        Assert.True(Eligible(task, empty, Noon("2026-08-02")));
    }

    [Fact]
    public void A_recurring_task_rejects_an_absolute_defer()
    {
        var task = Item(Noon("2026-08-01"), recurrence: Weekly(), defer: new AbsoluteDefer(Date("2026-08-23")));
        var empty = CompletionLog.Empty(Id);

        Assert.Throws<InvalidOperationException>(
            () => DeferRules.ResolvedFor(task, empty, Noon("2026-08-27"), Boundary));
        Assert.Throws<InvalidOperationException>(() => Eligible(task, empty, Noon("2026-08-27")));
    }

    // ── Derived Tasks (controller ruling) ─────────────────────────────────────────────

    [Fact]
    public void A_task_with_non_null_provenance_is_never_unprocessed_and_never_stale()
    {
        var now = Noon("2026-08-27");
        var provenance = new DerivedProvenance(new RuleId("tell-them-you-are-out"), "evt_01");
        var empty = CompletionLog.Empty(Id);

        // No Duration and 59 days old — both triggers, neither of which can reach it.
        var derived = Item(now.AddDays(-59), tags: TagSet.Empty, provenance: provenance);
        Assert.Equal(Status.Active, Of(derived, empty, now));

        // The same Task without its Provenance reads `Unprocessed`, so both rules really fired.
        Assert.Equal(Status.Unprocessed, Of(derived with { Provenance = null }, empty, now));
        Assert.Equal(Status.Stale, Of(derived with { Provenance = null, Tags = Duration("30") }, empty, now));

        // Completion still outranks: marking it done is the only interaction it has.
        Assert.Equal(Status.Done, Of(derived, Log(new CompletionEntry(null, Noon("2026-08-26"))), now));
    }

    [Fact]
    public void A_task_with_non_null_provenance_cannot_be_postponed()
    {
        var createdAt = Noon("2026-08-20");
        var derived = Item(createdAt, provenance: new DerivedProvenance(new RuleId("tell-them-you-are-out"), "evt_01"));

        Assert.False(StatusRules.CanPostpone(derived));

        // A plain one-off can; a recurring Task cannot either, since an absolute Postpone on one
        // is unrepresentable.
        Assert.True(StatusRules.CanPostpone(Item(createdAt)));
        Assert.False(StatusRules.CanPostpone(Item(createdAt, recurrence: Weekly())));
    }
}
