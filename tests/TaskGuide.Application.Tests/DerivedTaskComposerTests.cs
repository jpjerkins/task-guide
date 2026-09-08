using OneOf;
using TaskGuide.Application.Ports;
using TaskGuide.Application.Rules;
using TaskGuide.Application.Tasks;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Rules;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.Domain.Time;
using TaskGuide.TestSupport;
using Xunit;

namespace TaskGuide.Application.Tests;

public sealed class DerivedTaskComposerTests
{
    private static readonly DayBoundary Boundary = new(TimeZoneInfo.Utc);
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_derived_completion_entry_suppresses_the_matching_derived_Task()
    {
        var @event = TaggedEvent();
        var completion = new DerivedCompletionEntry(TagDeclaredRule.TimeOff.Id, @event.Id.Value, @event.Date.AddDays(-21), Now);
        var composed = Composer().Compose(new FakeStoreViewBuilder()
            .WithEvents([@event])
            .WithDerivedCompletions([completion])
            .Build());

        Assert.Empty(composed);
    }

    [Fact]
    public async Task A_TasksWrite_after_a_Compose_backed_read_contains_only_stored_Tasks()
    {
        var @event = TaggedEvent();
        var store = new FakeStore(new FakeStoreViewBuilder().WithEvents([@event]).Build());
        var stored = StoredTask();

        await store.MutateAsync<Never>(view =>
        {
            Assert.Single(Composer().Compose(view));
            return OneOf<StoreMutation, Never>.FromT0(new StoreMutation([new TasksWrite([.. view.Tasks, stored])]));
        }, CancellationToken.None);

        Assert.Equal([stored], store.Read().Tasks);
    }

    [Fact]
    public async Task Completing_a_derived_Task_writes_a_derived_completion_without_persisting_the_Task()
    {
        var @event = TaggedEvent();
        var store = new FakeStore(new FakeStoreViewBuilder().WithEvents([@event]).Build());
        var composer = Composer();
        var task = Assert.Single(composer.Compose(store.Read()));

        var result = await new CompleteTask(
            store, KnownDimensions.Default, new StaleThresholds(TimeSpan.FromDays(30), 3),
            new FixedTimeProvider(Now), Boundary, composer).ExecuteAsync(task.Id, CancellationToken.None);

        Assert.IsType<CompletionRecorded>(result.Value);
        Assert.Single(store.Read().DerivedCompletions);
        Assert.Empty(store.Read().Tasks);
    }

    private static DerivedTaskComposer Composer() =>
        new([TagDeclaredRule.TimeOff], new FakeDayShapeReader(), Boundary, new FixedTimeProvider(Now));

    private static Event TaggedEvent() => new(
        new EventId("evt_trip"),
        new DateOnly(2026, 10, 7),
        "Family trip",
        new TimeOnly(9, 0),
        new TimeOnly(10, 0),
        new TagSet(new Dictionary<DimensionId, IReadOnlyList<TagValue>>(), [new LooseTag("timeoff")]),
        AbsenceNotice: null);

    private static TaskItem StoredTask() => new(
        new TaskId("t_stored"),
        "Stored task",
        Notes: null,
        TagSet.Empty,
        Deadline: null,
        Defer: null,
        Postpone: null,
        Recurrence: null,
        CreatedAt: Now);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
