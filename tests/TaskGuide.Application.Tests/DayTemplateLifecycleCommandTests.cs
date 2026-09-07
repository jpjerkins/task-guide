using TaskGuide.Application.Ports;
using TaskGuide.Application.Schedule;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Time;
using TaskGuide.TestSupport;
using Xunit;

namespace TaskGuide.Application.Tests;

public sealed class DayTemplateLifecycleCommandTests
{
    private static readonly DateOnly Today = new(2026, 9, 6);
    private static readonly DayTemplateId TemplateId = new("dt_christmas");
    private static readonly AvailabilityWindow FamilyWindow = new(
        new WindowId("w_family"), "Family time", new TimeOnly(10, 0), new TimeOnly(20, 0), TagSet.Empty);

    [Fact]
    public async Task Promoting_a_one_off_day_writes_the_source_dates_use_record_and_does_not_re_link()
    {
        var source = new DateOverride(Today, [FamilyWindow], null);
        var store = new FakeStore(new FakeStoreViewBuilder().WithOverrides([source]).Build());
        var command = new PromoteOneOffDay(store);
        var template = new DayTemplate(TemplateId, "Christmas", [FamilyWindow], []);

        var result = await command.ExecuteAsync(Today, template, CancellationToken.None);

        var promoted = Assert.IsType<DayTemplate>(result.AsT0);
        Assert.Equal(FamilyWindow, Assert.Single(promoted.Windows));
        var mutation = Assert.Single(store.Mutations);
        Assert.Collection(
            mutation.OrderedWrites,
            write => Assert.IsType<OverridesWrite>(write),
            write => Assert.IsType<DayTemplatesWrite>(write));
        var sourceAfterPromotion = Assert.Single(store.Read().Overrides);
        Assert.Equal(new DayTemplateUse(TemplateId, "Christmas"), sourceAfterPromotion.Used);
        Assert.Equal("Family time", Assert.Single(sourceAfterPromotion.Windows).Name);

        var edited = promoted with { Windows = [FamilyWindow with { Name = "Edited elsewhere" }] };
        await store.MutateAsync<Never>(_ => new StoreMutation([new DayTemplatesWrite([edited])]), CancellationToken.None);

        Assert.Equal("Family time", Assert.Single(store.Read().Overrides).Windows.Single().Name);
    }

    [Fact]
    public async Task Re_stamping_replaces_the_use_record_rather_than_appending()
    {
        var first = new DayTemplate(new DayTemplateId("dt_first"), "First", [FamilyWindow], []);
        var secondWindow = FamilyWindow with { Id = new WindowId("w_second"), Name = "Second" };
        var second = new DayTemplate(new DayTemplateId("dt_second"), "Second", [secondWindow], []);
        var existing = new DateOverride(Today, [FamilyWindow], new DayTemplateUse(first.Id, first.Name));
        var store = new FakeStore(new FakeStoreViewBuilder().WithDayTemplates([first, second]).WithOverrides([existing]).Build());

        var result = await new StampDayTemplate(store).ExecuteAsync(Today, second.Id, CancellationToken.None);

        var stamped = result.AsT0;
        Assert.Equal(new DayTemplateUse(second.Id, second.Name), stamped.Used);
        Assert.Equal(secondWindow, Assert.Single(stamped.Windows));
        Assert.Equal(stamped, Assert.Single(store.Read().Overrides));
    }

    [Fact]
    public async Task Put_overrides_date_stamp_is_refused_for_an_unknown_template_id()
    {
        var store = new FakeStore();

        var result = await new StampDayTemplate(store).ExecuteAsync(Today, new DayTemplateId("dt_missing"), CancellationToken.None);

        Assert.True(result.IsT1);
        Assert.Equal("Day template was not found", result.AsT1.Reason);
        Assert.Empty(store.Mutations);
        Assert.Equal(1, store.RefusalCount);
    }

    [Fact]
    public async Task DELETE_day_templates_id_is_refused_while_the_template_is_in_use_and_accepted_when_it_is_Unused()
    {
        var template = new DayTemplate(TemplateId, "Christmas", [FamilyWindow], []);
        var used = new DateOverride(Today, [FamilyWindow], new DayTemplateUse(TemplateId, template.Name));
        var usedStore = new FakeStore(new FakeStoreViewBuilder().WithDayTemplates([template]).WithOverrides([used]).Build());
        var clock = new FakeTimeProvider(Today);

        var boundary = new DayBoundary(TimeZoneInfo.FindSystemTimeZoneById(DayBoundary.ZoneId));
        var refused = await new DeleteDayTemplate(usedStore, clock, boundary).ExecuteAsync(TemplateId, CancellationToken.None);

        Assert.True(refused.IsT1);
        Assert.Equal("Day template is in use", refused.AsT1.Reason);
        Assert.Empty(usedStore.Mutations);

        var unusedStore = new FakeStore(new FakeStoreViewBuilder().WithDayTemplates([template]).WithPatterns(new PatternBook(new PatternId("p_empty"), [])).Build());
        var deleted = await new DeleteDayTemplate(unusedStore, clock, boundary).ExecuteAsync(TemplateId, CancellationToken.None);

        Assert.True(deleted.IsT0);
        Assert.Empty(unusedStore.Read().DayTemplates);
        Assert.IsType<DayTemplatesWrite>(Assert.Single(Assert.Single(unusedStore.Mutations).OrderedWrites));
    }

    private sealed class FakeTimeProvider(DateOnly date) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }
}
