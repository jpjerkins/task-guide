using TaskGuide.Domain.Common;
using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.TestSupport;
using Xunit;

namespace TaskGuide.Application.Tests;

/// <summary>
/// `tests/TEST-INVENTORY.md`, "Test support (#77)": <see cref="TaskGuide.TestSupport.FakeStoreView"/>
/// and its builder never throw, unlike the private fakes each lane used to hand-roll — most
/// members default to empty, and <c>Patterns</c>/<c>DayTemplates</c> default to a resolvable
/// Pattern instead, since "empty" there is what used to throw.
/// </summary>
public sealed class FakeStoreViewTests
{
    private static TaskItem NewTask(string id, string title) =>
        new(new TaskId(id), title, null, TagSet.Empty, null, null, null, null, DateTimeOffset.UtcNow);

    [Fact]
    public void An_unseeded_FakeStoreView_reads_empty_on_every_member_except_the_default_Pattern()
    {
        var view = new FakeStoreViewBuilder().Build();

        Assert.Empty(view.Tasks);
        Assert.Empty(view.DerivedCompletions);
        Assert.Empty(view.Overrides);
        Assert.Empty(view.Events);
        Assert.Empty(view.EventExceptions);
    }

    /// <summary>
    /// #77 review finding 1: <c>PatternBook.Active</c> throws when the active id matches no
    /// Pattern, and <c>DayShapeReader.For</c> — the central read path every later lane
    /// exercises — opens by reading it. An unseeded view must not hand that reader a Pattern
    /// book it cannot resolve, so the default Pattern's active id must match a Pattern, that
    /// Pattern's weekday slots must all name a real Day template, and that template must be
    /// present in <see cref="TaskGuide.TestSupport.FakeStoreView.DayTemplates"/> — the same
    /// three steps <c>DayShapeReader.For</c> takes, walked here without depending on
    /// <c>TaskGuide.Infrastructure</c>.
    /// </summary>
    [Fact]
    public void An_unseeded_FakeStoreViews_default_Pattern_resolves_to_a_Day_template_present_in_DayTemplates()
    {
        var view = new FakeStoreViewBuilder().Build();

        var active = view.Patterns.Active;
        foreach (DayOfWeek weekday in Enum.GetValues<DayOfWeek>())
        {
            var templateId = active[weekday];
            var template = Assert.Single(view.DayTemplates, t => t.Id == templateId);
            Assert.NotNull(template);
        }
    }

    [Fact]
    public void A_seeded_FakeStoreView_reads_back_exactly_what_it_was_given()
    {
        var task = NewTask("t_01ARZ3NDEKTSV4RRFFQ69G5FAV", "Water the plants");
        var template = new DayTemplate(new DayTemplateId("dt_workday"), "Workday", [], []);
        var pattern = new Pattern(new PatternId("p_default"), "Default", [
            template.Id, template.Id, template.Id, template.Id, template.Id, template.Id, template.Id,
        ]);
        var patterns = new PatternBook(pattern.Id, [pattern]);
        var overrideDate = new DateOnly(2026, 9, 5);
        var dateOverride = new DateOverride(overrideDate, [], null);
        var evt = new Event(new EventId("evt_01ARZ3NDEKTSV4RRFFQ69G5N00"), overrideDate, "Band concert", new TimeOnly(18, 0), new TimeOnly(19, 0), TagSet.Empty, null);
        var exception = new EventException(overrideDate, new EventPrototypeId("ep_recital"), true, null, null, null);
        var derived = new DerivedCompletionEntry(new RuleId("rule_timeoff"), "trigger_1", overrideDate, DateTimeOffset.UtcNow);

        var view = new FakeStoreViewBuilder()
            .WithTasks([task])
            .WithDayTemplates([template])
            .WithPatterns(patterns)
            .WithOverrides([dateOverride])
            .WithEvents([evt])
            .WithEventExceptions([exception])
            .WithDerivedCompletions([derived])
            .Build();

        Assert.Same(task, Assert.Single(view.Tasks));
        Assert.Same(template, Assert.Single(view.DayTemplates));
        Assert.Same(patterns, view.Patterns);
        Assert.Same(dateOverride, Assert.Single(view.Overrides));
        Assert.Same(evt, Assert.Single(view.Events));
        Assert.Same(exception, Assert.Single(view.EventExceptions));
        Assert.Same(derived, Assert.Single(view.DerivedCompletions));
    }

    [Fact]
    public void CompletionsFor_an_unseeded_task_is_an_empty_log_not_a_throw()
    {
        var view = new FakeStoreViewBuilder().Build();
        var taskId = new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5FAV");

        var log = view.CompletionsFor(taskId);

        Assert.Equal(taskId, log.TaskId);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public void FiresOn_an_unseeded_date_is_empty_not_a_throw()
    {
        var view = new FakeStoreViewBuilder().Build();
        var date = new DateOnly(2026, 9, 5);

        var fires = view.FiresOn(date);

        Assert.Equal(date, fires.Date);
        Assert.Empty(fires.Rows);
    }

    /// <summary>
    /// #116 finding 1: <c>WithDayTemplates</c> used to leave the builder's default Pattern book
    /// naming the old default Day template, orphaning it. The builder's own default now re-points
    /// itself at the first seeded template, so a view seeded with templates alone still resolves.
    /// </summary>
    [Fact]
    public void WithDayTemplates_re_points_the_builders_default_Pattern_at_the_first_seeded_template()
    {
        var mine = new DayTemplate(new DayTemplateId("dt_mine"), "My day", [], []);
        var other = new DayTemplate(new DayTemplateId("dt_other"), "Other day", [], []);

        var view = new FakeStoreViewBuilder().WithDayTemplates([mine, other]).Build();

        var active = view.Patterns.Active;
        foreach (DayOfWeek weekday in Enum.GetValues<DayOfWeek>())
        {
            Assert.Equal(mine.Id, active[weekday]);
        }
    }

    /// <summary>#116 finding 1: an explicit `WithPatterns` wins over the builder's re-pointing,
    /// regardless of which `With…` call came first.</summary>
    [Fact]
    public void An_explicit_WithPatterns_wins_over_that_re_pointing_in_either_call_order()
    {
        var mine = new DayTemplate(new DayTemplateId("dt_mine"), "My day", [], []);
        var pattern = new Pattern(new PatternId("p_mine"), "Mine", Enumerable.Repeat(mine.Id, 7).ToArray());
        var book = new PatternBook(pattern.Id, [pattern]);

        var patternsThenTemplates = new FakeStoreViewBuilder().WithPatterns(book).WithDayTemplates([mine]).Build();
        var templatesThenPatterns = new FakeStoreViewBuilder().WithDayTemplates([mine]).WithPatterns(book).Build();

        Assert.Same(book, patternsThenTemplates.Patterns);
        Assert.Same(book, templatesThenPatterns.Patterns);
    }
}
