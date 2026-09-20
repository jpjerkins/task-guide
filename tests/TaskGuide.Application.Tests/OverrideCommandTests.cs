using TaskGuide.Application.Schedule;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.TestSupport;
using Xunit;

namespace TaskGuide.Application.Tests;

public sealed class OverrideCommandTests
{
    [Fact]
    public async Task Creating_an_Override_span_writes_one_Override_per_date_inclusive_of_both_ends()
    {
        var template = new DayTemplate(new DayTemplateId("dt_span"), "Weekend", [Window("w_weekend")], []);
        var store = new FakeStore(new FakeStoreViewBuilder().WithDayTemplates([template]).Build());

        var result = await new CreateOverrideSpan(store).ExecuteAsync(
            new OverrideSpanCommandRequest(
                new DateOnly(2026, 12, 24),
                new DateOnly(2026, 12, 26),
                (OverrideSpanMode)new StampOverrideSpan(template.Id)),
            CancellationToken.None);

        Assert.True(result.IsT0);
        Assert.Equal([new DateOnly(2026, 12, 24), new DateOnly(2026, 12, 25), new DateOnly(2026, 12, 26)],
            store.Read().Overrides.Select(overrideDay => overrideDay.Date).ToArray());
        Assert.All(store.Read().Overrides, overrideDay => Assert.Equal(new DayTemplateUse(template.Id, template.Name), overrideDay.Used));
    }

    [Fact]
    public async Task Freezing_an_Override_span_copies_each_dates_current_windows_preserves_their_ids_and_retains_an_existing_stamped_Overrides_use_record()
    {
        var thursday = new DayTemplate(new DayTemplateId("dt_thursday"), "Thursday", [Window("w_thursday")], []);
        var friday = new DayTemplate(new DayTemplateId("dt_friday"), "Friday", [Window("w_friday")], []);
        var saturday = new DayTemplate(new DayTemplateId("dt_saturday"), "Saturday", [Window("w_saturday")], []);
        var patternId = new PatternId("p_week");
        var days = Enumerable.Repeat(thursday.Id, 7).ToArray();
        days[(int)DayOfWeek.Friday] = friday.Id;
        days[(int)DayOfWeek.Saturday] = saturday.Id;
        var overriddenFriday = new DateOnly(2026, 12, 25);
        var stampedFriday = DayTemplateLifecycle.Stamp(overriddenFriday, friday);
        var existingFriday = stampedFriday with { Windows = [Window("w_override")] };
        var store = new FakeStore(new FakeStoreViewBuilder()
            .WithDayTemplates([thursday, friday, saturday])
            .WithPatterns(new PatternBook(patternId, [new Pattern(patternId, "Week", days)]))
            .WithOverrides([existingFriday])
            .Build());

        var result = await new CreateOverrideSpan(store).ExecuteAsync(
            new OverrideSpanCommandRequest(
                new DateOnly(2026, 12, 24),
                new DateOnly(2026, 12, 26),
                (OverrideSpanMode)new FreezeOverrideSpan()),
            CancellationToken.None);

        Assert.True(result.IsT0);
        Assert.Equal(
            ["w_thursday", "w_override", "w_saturday"],
            store.Read().Overrides
                .OrderBy(overrideDay => overrideDay.Date)
                .SelectMany(overrideDay => overrideDay.Windows)
                .Select(window => window.Id.Value)
                .ToArray());
        Assert.Equal(existingFriday.Used, Assert.Single(store.Read().Overrides, overrideDay => overrideDay.Date == overriddenFriday).Used);
    }

    [Fact]
    public async Task An_Override_span_ending_at_DateOnly_MaxValue_is_written()
    {
        var date = DateOnly.MaxValue;
        var store = new FakeStore();

        var result = await new CreateOverrideSpan(store).ExecuteAsync(
            new OverrideSpanCommandRequest(date, date, (OverrideSpanMode)new BlankOverrideSpan()),
            CancellationToken.None);

        Assert.True(result.IsT0);
        Assert.Equal(date, Assert.Single(store.Read().Overrides).Date);
    }

    /// <summary>Beyond-inventory: a one-date span yields exactly that date.</summary>
    [Fact]
    public void An_Override_span_of_one_date_yields_exactly_that_date()
    {
        var date = new DateOnly(2026, 8, 28);
        var request = new OverrideSpanCommandRequest(date, date, (OverrideSpanMode)new BlankOverrideSpan());

        var dates = request.Dates().ToList();

        Assert.Equal([date], dates);
    }

    /// <summary>Beyond-inventory: a multi-date span yields every date inclusive of both ends, ascending.</summary>
    [Fact]
    public void An_Override_span_yields_every_date_inclusive_of_both_ends_in_ascending_order()
    {
        var from = new DateOnly(2026, 8, 28);
        var to = from.AddDays(3);
        var request = new OverrideSpanCommandRequest(from, to, (OverrideSpanMode)new BlankOverrideSpan());

        var dates = request.Dates().ToList();

        Assert.Equal(
            [from, from.AddDays(1), from.AddDays(2), to],
            dates);
    }

    [Fact]
    public async Task Editing_a_stamped_Override_makes_it_a_one_off_day_and_preserves_its_use_record()
    {
        var template = new DayTemplate(new DayTemplateId("dt_christmas"), "Christmas", [Window("w_original")], []);
        var date = new DateOnly(2026, 12, 25);
        var stamped = DayTemplateLifecycle.Stamp(date, template);
        var store = new FakeStore(new FakeStoreViewBuilder().WithOverrides([stamped]).Build());
        var editedWindow = Window("w_edited");

        var result = await new EditOverride(store).ExecuteAsync(date, [editedWindow], CancellationToken.None);

        Assert.True(result.IsT0);
        var edited = Assert.Single(store.Read().Overrides);
        Assert.Equal(editedWindow, Assert.Single(edited.Windows));
        Assert.Equal(stamped.Used, edited.Used);
    }

    [Fact]
    public async Task Deleting_an_Override_removes_it()
    {
        var date = new DateOnly(2026, 12, 25);
        var other = new DateOnly(2026, 12, 26);
        var store = new FakeStore(new FakeStoreViewBuilder().WithOverrides([
            new DateOverride(date, [], null),
            new DateOverride(other, [], null),
        ]).Build());

        var result = await new DeleteOverride(store).ExecuteAsync(date, CancellationToken.None);

        Assert.True(result.IsT0);
        Assert.Equal(other, Assert.Single(store.Read().Overrides).Date);
    }

    [Fact]
    public async Task Deleting_an_Override_for_a_date_with_none_is_refused()
    {
        var store = new FakeStore();

        var result = await new DeleteOverride(store).ExecuteAsync(new DateOnly(2026, 12, 25), CancellationToken.None);

        Assert.True(result.IsT1);
    }

    private static AvailabilityWindow Window(string id) => new(
        new WindowId(id), "Family time", new TimeOnly(10, 0), new TimeOnly(20, 0), TagSet.Empty);
}
