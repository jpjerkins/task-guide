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
/// and its builder default to empty, never to throw, unlike the private fakes each lane used to
/// hand-roll.
/// </summary>
public sealed class FakeStoreViewTests
{
    private static TaskItem NewTask(string id, string title) =>
        new(new TaskId(id), title, null, TagSet.Empty, null, null, null, null, DateTimeOffset.UtcNow);

    [Fact]
    public void An_unseeded_FakeStoreView_reads_empty_on_every_member()
    {
        var view = new FakeStoreViewBuilder().Build();

        Assert.Empty(view.Tasks);
        Assert.Empty(view.DerivedCompletions);
        Assert.Empty(view.DayTemplates);
        Assert.Empty(view.Patterns.Patterns);
        Assert.Empty(view.Overrides);
        Assert.Empty(view.Events);
        Assert.Empty(view.EventExceptions);
    }

    [Fact]
    public void A_seeded_FakeStoreView_reads_back_exactly_what_it_was_given()
    {
        var task = NewTask("t_01ARZ3NDEKTSV4RRFFQ69G5FAV", "Water the plants");
        var template = new DayTemplate(new DayTemplateId("dt_workday"), "Workday", [], []);
        var pattern = new Pattern(new PatternId("pat_default"), "Default", [
            template.Id, template.Id, template.Id, template.Id, template.Id, template.Id, template.Id,
        ]);
        var patterns = new PatternBook(pattern.Id, [pattern]);
        var overrideDate = new DateOnly(2026, 9, 5);
        var dateOverride = new DateOverride(overrideDate, [], null);
        var evt = new Event(new EventId("evt_01ARZ3NDEKTSV4RRFFQ69G5N00"), overrideDate, "Band concert", new TimeOnly(18, 0), new TimeOnly(19, 0), TagSet.Empty, null);
        var exception = new EventException(overrideDate, new EventPrototypeId("evtp_recital"), true, null, null, null);
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
}
