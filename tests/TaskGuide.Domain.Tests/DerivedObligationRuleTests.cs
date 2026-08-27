using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Rules;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.Domain.Time;
using Xunit;

namespace TaskGuide.Domain.Tests;

/// <summary>
/// `tests/TEST-INVENTORY.md`'s "Derived-obligation rules" section.
/// </summary>
public sealed class DerivedObligationRuleTests
{
    private static readonly DayBoundary Boundary = new(TimeZoneInfo.FindSystemTimeZoneById(DayBoundary.ZoneId));
    private static readonly ClockTimeResolution Resolution = new(Boundary);
    private static readonly DimensionRegistry Registry = KnownDimensions.Default;

    /// <summary>Tuesday.</summary>
    private static readonly DateOnly Today = new(2026, 9, 1);

    /// <summary>The Saturday of Sam's tournament, seven weeks out.</summary>
    private static readonly DateOnly TournamentDate = new(2026, 10, 10);

    private static readonly DateOnly FirstSunday = new(2026, 9, 6);
    private static readonly DateOnly SecondSunday = new(2026, 9, 13);
    private static readonly DateOnly ThirdSunday = new(2026, 9, 20);

    private static readonly EventPrototypeId MinistryId = new("ep_ministry");
    private static readonly DayTemplateId SundayTemplate = new("dt_sunday");
    private static readonly DayTemplateId WeekdayTemplate = new("dt_weekday");

    // ---- the trigger side ------------------------------------------------

    /// <summary>The dated Event that declares `#timeoff`, tagged `With whom: Sam`.</summary>
    private static Event Tournament(DateOnly? date = null, bool tagged = true) => new(
        new EventId("evt_tournament"),
        date ?? TournamentDate,
        "Sam's tournament",
        new TimeOnly(8, 0),
        new TimeOnly(17, 0),
        new TagSet(
            new Dictionary<DimensionId, IReadOnlyList<TagValue>>
            {
                [KnownDimensions.WithWhom] = [new TagValue("sam")],
            },
            tagged ? [new LooseTag("timeoff")] : Array.Empty<LooseTag>()),
        AbsenceNotice: null);

    /// <summary>Student ministry: 9–11 on Sundays, owed a week's notice if it is missed.</summary>
    private static readonly EventPrototype Ministry = new(
        MinistryId,
        "Student ministry",
        new TimeOnly(9, 0),
        new TimeOnly(11, 0),
        TagSet.Empty,
        new BeforeOffset(1, OffsetUnit.Weeks));

    /// <summary>The Sunday instance the Pattern assumes, as it appears in a date's actual shape.</summary>
    private static Event MinistryOn(DateOnly date, TimeOnly? start = null, string? name = null) => new(
        new EventId("evt_ministry_instance"),
        date,
        name ?? Ministry.Name,
        start ?? Ministry.Start,
        (start ?? Ministry.Start).AddHours(2),
        TagSet.Empty,
        Ministry.AbsenceNotice);

    private static readonly PatternBook Patterns = new(
        new PatternId("p_active"),
        [
            new Pattern(new PatternId("p_active"), "Term time",
            [
                SundayTemplate,   // Sunday
                WeekdayTemplate,  // Monday
                WeekdayTemplate,
                WeekdayTemplate,
                WeekdayTemplate,
                WeekdayTemplate,
                WeekdayTemplate,  // Saturday
            ]),
        ]);

    private static readonly IReadOnlyList<DayTemplate> Templates =
    [
        new DayTemplate(SundayTemplate, "Sunday", Array.Empty<AvailabilityWindow>(), [Ministry]),
        new DayTemplate(WeekdayTemplate, "Weekday", Array.Empty<AvailabilityWindow>(), Array.Empty<EventPrototype>()),
    ];

    private static DateOverride TravelDay(DateOnly date) =>
        new(date, Array.Empty<AvailabilityWindow>(), Used: null);

    // ---- the rules under test --------------------------------------------

    private static readonly TagDeclaredRule TimeOff = TagDeclaredRule.TimeOff;
    private static readonly AbsenceRule Absence = new();

    /// <summary>The only view of the calendar; reading one never writes one.</summary>
    private sealed class FakeShapes(Func<DateOnly, IReadOnlyList<Event>> eventsOn) : IDayShapeReader
    {
        public List<DateOnly> DatesRead { get; } = [];

        public DayShape For(DateOnly date)
        {
            DatesRead.Add(date);
            return new DayShape(date, Array.Empty<AvailabilityWindow>(), eventsOn(date), IsOverridden: false);
        }
    }

    private static FakeShapes NoEvents() => new(_ => Array.Empty<Event>());

    /// <summary>Every Sunday keeps its ministry instance unless the date says otherwise.</summary>
    private static FakeShapes MinistryEverySunday(params DateOnly[] except) => new(date =>
        date.DayOfWeek is DayOfWeek.Sunday && !except.Contains(date)
            ? [MinistryOn(date)]
            : Array.Empty<Event>());

    private static DerivedObligationContext Context(
        DateOnly? today = null,
        IReadOnlyList<Event>? datedEvents = null,
        IReadOnlyList<DateOverride>? overrides = null,
        IDayShapeReader? shapes = null,
        IReadOnlyList<DerivedCompletionEntry>? completions = null,
        IReadOnlyList<EventException>? exceptions = null) =>
        new(
            today ?? Today,
            datedEvents ?? Array.Empty<Event>(),
            overrides ?? Array.Empty<DateOverride>(),
            shapes ?? NoEvents(),
            completions ?? Array.Empty<DerivedCompletionEntry>())
        {
            Patterns = Patterns,
            DayTemplates = Templates,
            EventExceptions = exceptions ?? Array.Empty<EventException>(),
            Boundary = Boundary,
        };

    // ---- the tag-declared family -----------------------------------------

    [Fact]
    public void A_rule_reads_a_dated_record_and_produces_a_read_only_Task_carrying_provenance()
    {
        var derived = Assert.Single(TimeOff.Derive(Context(datedEvents: [Tournament()])));

        Assert.Equal("Ask off work", derived.Title);
        Assert.Equal(new DateOnly(2026, 9, 19), derived.Deadline);
        Assert.Equal(new DerivedProvenance(TimeOff.Id, "evt_tournament"), derived.Provenance);

        // Read-only: a projection of the trigger, with no lifecycle of its own.
        Assert.Null(derived.Recurrence);
        Assert.Null(derived.Postpone);
    }

    [Fact]
    public void Tags_are_never_inherited_from_the_trigger()
    {
        var derived = Assert.Single(TimeOff.Derive(Context(datedEvents: [Tournament()])));

        // `With whom: Sam` would make the obligation near-unmatchable — a manufactured Orphan.
        Assert.Empty(derived.Tags.On(KnownDimensions.WithWhom));
        Assert.DoesNotContain(new LooseTag("timeoff"), derived.Tags.LooseTags);

        // The rule hard-codes the shape instead, Duration included.
        Assert.Equal(new TagValue("10"), derived.Tags.SingleOn(KnownDimensions.Duration));
    }

    [Fact]
    public void Moving_the_triggers_date_moves_the_obligations_Deadline()
    {
        var moved = Assert.Single(TimeOff.Derive(
            Context(datedEvents: [Tournament(date: new DateOnly(2026, 10, 17))])));

        Assert.Equal(new DateOnly(2026, 9, 26), moved.Deadline);
    }

    [Fact]
    public void Deleting_the_trigger_deletes_the_obligation_with_no_cleanup_pass()
    {
        Assert.Empty(TimeOff.Derive(Context(datedEvents: Array.Empty<Event>())));
    }

    [Fact]
    public void Removing_the_triggering_Tag_deletes_the_obligation()
    {
        Assert.Empty(TimeOff.Derive(Context(datedEvents: [Tournament(tagged: false)])));
    }

    [Fact]
    public void A_completion_keyed_ruleId_triggerId_due_stops_matching_when_the_trigger_moves()
    {
        var logged = new DerivedCompletionEntry(
            TimeOff.Id, "evt_tournament", new DateOnly(2026, 9, 19), Resolution.Resolve(Today, new TimeOnly(9, 0)));

        // Complete it where it stands, and it goes away.
        Assert.Empty(TimeOff.Derive(Context(datedEvents: [Tournament()], completions: [logged])));

        // Move the tournament and the logged `due` no longer matches the live one, so the
        // obligation re-derives — nothing had to notice the move.
        var reDerived = Assert.Single(TimeOff.Derive(Context(
            datedEvents: [Tournament(date: new DateOnly(2026, 10, 17))],
            completions: [logged])));

        Assert.Equal(new DateOnly(2026, 9, 26), reDerived.Deadline);
    }

    [Fact]
    public void Past_its_own_Deadline_the_obligation_stays_live_at_maximum_urgency()
    {
        var today = new DateOnly(2026, 9, 25);  // past the 9/19 due, short of the 10/10 tournament
        var derived = Assert.Single(TimeOff.Derive(Context(today: today, datedEvents: [Tournament()])));

        // A passed Deadline is `UrgencyBand.DeadlinePassed` — the top band. Asking late beats
        // not asking, so the obligation is still here to rank.
        Assert.True(derived.Deadline < today);
        Assert.Equal(Status.Active, StatusOf(derived, today));
    }

    [Fact]
    public void Past_the_triggers_date_it_silently_stops_being_derived_with_no_Stale_and_no_count()
    {
        var derived = TimeOff.Derive(Context(today: new DateOnly(2026, 10, 11), datedEvents: [Tournament()]));

        // Not `Stale`, not counted: there is nothing left to be either.
        Assert.Empty(derived);
    }

    [Fact]
    public void A_recurring_Event_never_triggers()
    {
        // The instance exists in the day's shape but in no stored dated record.
        var shapes = new FakeShapes(date => date == TournamentDate
            ? [Tournament() with { Id = new EventId("evt_recurring_instance") }]
            : Array.Empty<Event>());

        Assert.Empty(TimeOff.Derive(Context(datedEvents: Array.Empty<Event>(), shapes: shapes)));

        // Triggers are found by scanning the sparse stored records, never by walking a calendar.
        Assert.Empty(shapes.DatesRead);
    }

    // ---- the absence family ----------------------------------------------

    [Fact]
    public void Absence_not_overlap_an_Event_merely_overlapping_the_commitment_derives_nothing()
    {
        // The tournament starts at 10 and generates a one-off day, but the 9:00 service is still
        // on the date's shape — it was left early, not skipped.
        var derived = Absence.Derive(Context(
            datedEvents: [Tournament(date: FirstSunday)],
            overrides: [TravelDay(FirstSunday)],
            shapes: MinistryEverySunday()));

        Assert.Empty(derived);
    }

    [Fact]
    public void An_Override_stamped_without_the_commitment_derives_it()
    {
        var derived = Assert.Single(Absence.Derive(Context(
            overrides: [TravelDay(FirstSunday)],
            shapes: MinistryEverySunday(FirstSunday))));

        Assert.Equal("Tell Student ministry you'll be out", derived.Title);
        Assert.Equal(new DateOnly(2026, 8, 30), derived.Deadline);
        Assert.Equal(Absence.Id, derived.Provenance?.RuleId);
    }

    [Fact]
    public void A_deleted_instance_Event_exception_derives_it()
    {
        var deleted = new[] { new EventException(FirstSunday, MinistryId, Deleted: true, null, null, null) };

        var derived = Assert.Single(Absence.Derive(Context(
            shapes: MinistryEverySunday(FirstSunday), exceptions: deleted)));

        Assert.Equal(new DateOnly(2026, 8, 30), derived.Deadline);

        // The exception is the fact, and the rule reads it directly. Deleting an instance does
        // not stamp an Override, so the date's Windows stay under live Pattern propagation and
        // whether a shape reader has layered the deletion is not this rule's business — the
        // honest "I'm not going" gesture is absence either way.
        Assert.Single(Absence.Derive(Context(shapes: MinistryEverySunday(), exceptions: deleted)));
    }

    [Fact]
    public void A_moved_instance_does_not()
    {
        // The honest "I'm not going" gesture is a delete; moving one week's commitment an hour
        // later is an ordinary thing to want, and it is not absence.
        var moved = new FakeShapes(date => date == FirstSunday
            ? [MinistryOn(date, start: new TimeOnly(14, 0))]
            : Array.Empty<Event>());

        Assert.Empty(Absence.Derive(Context(
            shapes: moved,
            exceptions:
            [
                new EventException(
                    FirstSunday, MinistryId, Deleted: false, null, new TimeOnly(14, 0), new TimeOnly(16, 0)),
            ])));
    }

    [Fact]
    public void A_moved_instance_on_an_Overridden_date_still_does_not_derive()
    {
        // The date carries an Override, so it *is* a candidate — the moved instance therefore
        // has to be recognised as presence rather than skipped for want of a trigger record.
        var moved = new FakeShapes(date => date == FirstSunday
            ? [MinistryOn(date, start: new TimeOnly(14, 0))]
            : Array.Empty<Event>());

        Assert.Empty(Absence.Derive(Context(
            overrides: [TravelDay(FirstSunday)],
            shapes: moved,
            exceptions:
            [
                new EventException(
                    FirstSunday, MinistryId, Deleted: false, null, new TimeOnly(14, 0), new TimeOnly(16, 0)),
            ])));
    }

    [Fact]
    public void A_renamed_instance_the_shape_still_carries_derives_nothing()
    {
        const string Renamed = "Student ministry (in the chapel)";

        var renamed = new FakeShapes(date => date == FirstSunday
            ? [MinistryOn(date, name: Renamed)]
            : Array.Empty<Event>());

        var exceptions = new[]
        {
            new EventException(FirstSunday, MinistryId, Deleted: false, Renamed, null, null),
        };

        // Nothing removed it, so it is not a candidate at all.
        Assert.Empty(Absence.Derive(Context(shapes: renamed, exceptions: exceptions)));

        // And when an Override does make the date a candidate, the exception is what says which
        // name the shape should be carrying — a rename is not an absence.
        Assert.Empty(Absence.Derive(Context(
            overrides: [TravelDay(FirstSunday)], shapes: renamed, exceptions: exceptions)));
    }

    [Fact]
    public void Three_contiguous_absences_derive_one_obligation_due_before_the_first()
    {
        var derived = Assert.Single(Absence.Derive(Context(
            overrides: [TravelDay(FirstSunday), TravelDay(SecondSunday), TravelDay(ThirdSunday)],
            shapes: MinistryEverySunday(FirstSunday, SecondSunday, ThirdSunday))));

        // You tell them once, a week before the first.
        Assert.Equal(new DateOnly(2026, 8, 30), derived.Deadline);
    }

    // ---- what a derived Task is ------------------------------------------

    [Fact]
    public void A_derived_Task_is_never_Unprocessed_and_never_Stale()
    {
        var derived = Assert.Single(TimeOff.Derive(Context(datedEvents: [Tournament()])));

        // Never `Unprocessed` because *the rule supplied a Duration*, not because Status exempts it.
        Assert.Equal(Status.Active, StatusOf(derived, Today));

        // Never `Stale`: its lifetime is bounded by its trigger, so the age clock has nothing to
        // say about it — even deadline-less and a year old, which would make an authored Task stale.
        var ancient = derived with
        {
            Deadline = null,
            CreatedAt = Resolution.Resolve(new DateOnly(2025, 1, 1), new TimeOnly(9, 0)),
        };

        Assert.Equal(Status.Active, StatusOf(ancient, Today));
        Assert.Equal(Status.Stale, StatusOf(ancient with { Provenance = null }, Today));
    }

    [Fact]
    public void A_derived_Task_cannot_be_postponed()
    {
        var derived = Assert.Single(TimeOff.Derive(Context(datedEvents: [Tournament()])));

        Assert.False(StatusRules.CanPostpone(derived));
        Assert.True(StatusRules.CanPostpone(derived with { Provenance = null }));
    }

    private static Status StatusOf(TaskItem task, DateOnly today) => StatusRules.Of(
        task,
        CompletionLog.Empty(task.Id),
        Registry,
        new StaleThresholds(TimeSpan.FromDays(60), 3),
        Resolution.Resolve(today, new TimeOnly(9, 0)),
        Boundary);
}
