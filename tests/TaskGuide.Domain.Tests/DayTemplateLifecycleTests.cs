using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using Xunit;

namespace TaskGuide.Domain.Tests;

/// <summary>
/// `tests/TEST-INVENTORY.md`'s "`Unused`" bullets, plus `OverrideSpanRequest.Dates()`.
/// </summary>
public sealed class DayTemplateLifecycleTests
{
    private static readonly DateOnly Today = new(2026, 8, 28);

    private static AvailabilityWindow Evening(string id = "w_evening") => new(
        new WindowId(id), "Evening", new TimeOnly(18, 0), new TimeOnly(19, 0), TagSet.Empty);

    private static readonly DayTemplateId Volleyball = new("dt_volleyball");
    private static readonly DayTemplateId Workday = new("dt_workday");

    private static Pattern PatternOf(DayTemplateId template, string id = "p_1") => new(
        new PatternId(id), "Pattern", [.. Enumerable.Repeat(template, 7)]);

    private static DateOverride Stamp(DateOnly date, DayTemplateId template, string name = "Volleyball Tuesday") =>
        new(date, [Evening()], new DayTemplateUse(template, name));

    private static DateOverride OneOffDay(DateOnly date) => new(date, [Evening()], null);

    /// <summary>
    /// `tests/TEST-INVENTORY.md`: "`Unused` is false for a template referenced only by a
    /// dormant Pattern". A Pattern is dormant simply by not being the active one; `IsUnused`
    /// takes every Pattern regardless, so a reference from any of them defeats `Unused`.
    /// </summary>
    [Fact]
    public void Unused_is_false_for_a_template_referenced_only_by_a_dormant_Pattern()
    {
        // The "active" Pattern (school-year weekdays) never mentions Volleyball; a second,
        // dormant Pattern (the summer season) does. Placing it second, and not the only
        // entry, matters: an implementation that only looked at the first/active Pattern would
        // still pass this test if the reference were the sole list entry.
        var active = PatternOf(Workday, "p_school_year");
        var dormant = PatternOf(Volleyball, "p_summer");

        var unused = DayTemplateLifecycle.IsUnused(Volleyball, [active, dormant], [], Today);

        Assert.False(unused);
    }

    /// <summary>
    /// `tests/TEST-INVENTORY.md`: "`Unused` is false for a template stamped within ±13 months,
    /// in either direction" — the direction into the past.
    /// </summary>
    [Fact]
    public void Unused_is_false_for_a_template_stamped_within_13_months_in_the_past()
    {
        var overrides = new[] { Stamp(Today.AddMonths(-13), Volleyball) };

        var unused = DayTemplateLifecycle.IsUnused(Volleyball, [], overrides, Today);

        Assert.False(unused);
    }

    /// <summary>
    /// `tests/TEST-INVENTORY.md`: "`Unused` is false for a template stamped within ±13 months,
    /// in either direction" — the direction into the future.
    /// </summary>
    [Fact]
    public void Unused_is_false_for_a_template_stamped_within_13_months_in_the_future()
    {
        var overrides = new[] { Stamp(Today.AddMonths(13), Volleyball) };

        var unused = DayTemplateLifecycle.IsUnused(Volleyball, [], overrides, Today);

        Assert.False(unused);
    }

    /// <summary>Beyond-inventory: the horizon is symmetric, so 14 months past falls outside it too.</summary>
    [Fact]
    public void A_template_stamped_14_months_ago_is_Unused()
    {
        var overrides = new[] { Stamp(Today.AddMonths(-14), Volleyball) };

        var unused = DayTemplateLifecycle.IsUnused(Volleyball, [], overrides, Today);

        Assert.True(unused);
    }

    /// <summary>Beyond-inventory: same, into the future.</summary>
    [Fact]
    public void A_template_stamped_14_months_ahead_is_Unused()
    {
        var overrides = new[] { Stamp(Today.AddMonths(14), Volleyball) };

        var unused = DayTemplateLifecycle.IsUnused(Volleyball, [], overrides, Today);

        Assert.True(unused);
    }

    /// <summary>
    /// `tests/TEST-INVENTORY.md`: "deleting an `Unused` template corrupts no record". An
    /// `Unused` template is reachable from nothing — no Pattern names it, and every Override
    /// holds a copy — so removing it from the caller's template list leaves every Override's
    /// Windows byte-identical.
    /// </summary>
    [Fact]
    public void Deleting_an_Unused_template_corrupts_no_record()
    {
        var templates = new List<DayTemplateId> { Volleyball, Workday };
        var unrelatedStamp = Stamp(Today.AddMonths(-1), Workday, "Workday");
        var oneOff = OneOffDay(Today.AddDays(3));
        var overrides = new[] { unrelatedStamp, oneOff };
        var windowsBefore = overrides.Select(o => o.Windows).ToList();

        var unused = DayTemplateLifecycle.IsUnused(Volleyball, [], overrides, Today);
        Assert.True(unused);

        // "Deleting" is dropping the id from the caller's templates list — there is no store
        // mutation to call, since an Override holds a copy rather than a reference to it.
        templates.Remove(Volleyball);

        Assert.DoesNotContain(Volleyball, templates);
        for (var i = 0; i < overrides.Length; i++)
        {
            Assert.Equal(windowsBefore[i], overrides[i].Windows);
        }
    }

    /// <summary>Beyond-inventory: a one-date span yields exactly that date.</summary>
    [Fact]
    public void An_Override_span_of_one_date_yields_exactly_that_date()
    {
        var request = new OverrideSpanRequest(Today, Today, null);

        var dates = request.Dates().ToList();

        Assert.Equal([Today], dates);
    }

    /// <summary>Beyond-inventory: a multi-date span yields every date inclusive of both ends, ascending.</summary>
    [Fact]
    public void An_Override_span_yields_every_date_inclusive_of_both_ends_in_ascending_order()
    {
        var from = Today;
        var to = Today.AddDays(3);
        var request = new OverrideSpanRequest(from, to, null);

        var dates = request.Dates().ToList();

        Assert.Equal(
            [from, from.AddDays(1), from.AddDays(2), to],
            dates);
    }
}
