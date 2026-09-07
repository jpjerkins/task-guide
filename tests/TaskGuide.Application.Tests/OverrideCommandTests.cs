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
            new OverrideSpanRequest(new DateOnly(2026, 12, 24), new DateOnly(2026, 12, 26), template.Id), CancellationToken.None);

        Assert.True(result.IsT0);
        Assert.Equal([new DateOnly(2026, 12, 24), new DateOnly(2026, 12, 25), new DateOnly(2026, 12, 26)],
            store.Read().Overrides.Select(overrideDay => overrideDay.Date).ToArray());
        Assert.All(store.Read().Overrides, overrideDay => Assert.Equal(new DayTemplateUse(template.Id, template.Name), overrideDay.Used));
    }

    [Fact]
    public async Task An_Override_span_ending_at_DateOnly_MaxValue_is_written()
    {
        var date = DateOnly.MaxValue;
        var store = new FakeStore();

        var result = await new CreateOverrideSpan(store).ExecuteAsync(new OverrideSpanRequest(date, date, null), CancellationToken.None);

        Assert.True(result.IsT0);
        Assert.Equal(date, Assert.Single(store.Read().Overrides).Date);
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

    private static AvailabilityWindow Window(string id) => new(
        new WindowId(id), "Family time", new TimeOnly(10, 0), new TimeOnly(20, 0), TagSet.Empty);
}
