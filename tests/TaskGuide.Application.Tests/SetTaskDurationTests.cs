using TaskGuide.Application.Tasks;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.Domain.Time;
using TaskGuide.TestSupport;
using Xunit;

namespace TaskGuide.Application.Tests;

public sealed class SetTaskDurationTests
{
    [Theory]
    [InlineData("2")]
    [InlineData("10")]
    [InlineData("30")]
    [InlineData("60")]
    [InlineData("longer")]
    public async Task Supplying_a_bucket_repairs_missing_Duration(string duration)
    {
        var task = MakeTask();
        var store = new FakeStore(new FakeStoreViewBuilder().WithTasks([task]).Build());
        var result = await new SetTaskDuration(store).ExecuteAsync(task.Id, duration, CancellationToken.None);
        Assert.True(result.IsT0);
        Assert.Equal(duration, Assert.Single(store.Read().Tasks).Tags.SingleOn(KnownDimensions.Duration)?.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Longer")]
    [InlineData(" 30")]
    [InlineData("30 ")]
    [InlineData("45")]
    [InlineData("0")]
    public async Task Invalid_Duration_is_refused_without_changing_the_store(string? duration)
    {
        var task = MakeTask();
        var store = new FakeStore(new FakeStoreViewBuilder().WithTasks([task]).Build());
        var before = store.Read();
        var result = await new SetTaskDuration(store).ExecuteAsync(task.Id, duration, CancellationToken.None);
        Assert.True(result.IsT1);
        Assert.Same(before, store.Read());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_derived_Tasks_are_refused_without_changing_the_store(bool derived)
    {
        var task = MakeTask() with { Provenance = new DerivedProvenance(new RuleId("absence"), "event_1") };
        var store = new FakeStore(new FakeStoreViewBuilder().WithTasks(derived ? [task] : []).Build());
        var before = store.Read();
        var result = await new SetTaskDuration(store).ExecuteAsync(task.Id, "30", CancellationToken.None);
        Assert.True(result.IsT2);
        Assert.NotEmpty(result.AsT2.Reason);
        Assert.Same(before, store.Read());
    }

    [Fact]
    public async Task Setting_Duration_replaces_only_that_dimension_and_preserves_the_rest_of_the_store()
    {
        var task = MakeTask() with
        {
            Title = "Keep this title",
            Notes = "Keep these notes",
            Tags = new TagSet(
                new Dictionary<DimensionId, IReadOnlyList<TagValue>>
                {
                    [KnownDimensions.Duration] = [new TagValue("10")],
                    [KnownDimensions.Location] = [new TagValue("garage")],
                },
                [new LooseTag("urgent")]),
        };
        var other = MakeTask() with { Id = new TaskId("t_other") };
        var store = new FakeStore(new FakeStoreViewBuilder().WithTasks([task, other]).Build());

        var result = await new SetTaskDuration(store).ExecuteAsync(task.Id, "30", CancellationToken.None);

        Assert.True(result.IsT0);
        var updated = Assert.Single(store.Read().Tasks, candidate => candidate.Id == task.Id);
        Assert.Equal(task with
        {
            Tags = task.Tags with
            {
                Dimensions = new Dictionary<DimensionId, IReadOnlyList<TagValue>>
                {
                    [KnownDimensions.Duration] = [new TagValue("30")],
                    [KnownDimensions.Location] = [new TagValue("garage")],
                },
            },
        }, updated);
        Assert.Equal(other, Assert.Single(store.Read().Tasks, candidate => candidate.Id == other.Id));
    }

    [Fact]
    public async Task Setting_the_same_Duration_bucket_twice_is_idempotent_and_succeeds()
    {
        var task = MakeTask();
        var store = new FakeStore(new FakeStoreViewBuilder().WithTasks([task]).Build());

        var first = await new SetTaskDuration(store).ExecuteAsync(task.Id, "30", CancellationToken.None);
        var afterFirst = Assert.Single(store.Read().Tasks);
        var second = await new SetTaskDuration(store).ExecuteAsync(task.Id, "30", CancellationToken.None);
        var afterSecond = Assert.Single(store.Read().Tasks);

        Assert.True(first.IsT0);
        Assert.True(second.IsT0);
        Assert.Equal(afterFirst, afterSecond);
    }

    [Fact]
    public async Task Setting_Duration_transitions_Status_from_Unprocessed_to_Active_without_persisting_Status()
    {
        var task = MakeTask();
        var store = new FakeStore(new FakeStoreViewBuilder().WithTasks([task]).Build());
        var now = DateTimeOffset.UtcNow;
        var boundary = new DayBoundary(TimeZoneInfo.Utc);
        var thresholds = new StaleThresholds(TimeSpan.FromDays(60), 3);

        Assert.Equal(Status.Unprocessed, StatusRules.Of(task, CompletionLog.Empty(task.Id), KnownDimensions.Default, thresholds, now, boundary));

        var result = await new SetTaskDuration(store).ExecuteAsync(task.Id, "30", CancellationToken.None);
        var updated = Assert.Single(store.Read().Tasks);

        Assert.True(result.IsT0);
        Assert.Equal(Status.Active, StatusRules.Of(updated, CompletionLog.Empty(task.Id), KnownDimensions.Default, thresholds, now, boundary));
        Assert.DoesNotContain("Status", updated.ToString(), StringComparison.Ordinal);
    }

    private static TaskItem MakeTask() => new(new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5FAW"),
        "Sort garage", "Keep notes", TagSet.Empty, null, null, null, null, DateTimeOffset.UtcNow);
}
