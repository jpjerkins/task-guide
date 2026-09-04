using OneOf;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.TestSupport;
using Xunit;

namespace TaskGuide.Application.Tests;

/// <summary>
/// `tests/TEST-INVENTORY.md`, "Test support (#77)": <see cref="TaskGuide.TestSupport.FakeStore"/>
/// records and applies against the view it holds right now, matching the real store's contract
/// of gating inside the write lock.
/// </summary>
public sealed class FakeStoreTests
{
    private static TaskItem NewTask(string id, string title) =>
        new(new TaskId(id), title, null, TagSet.Empty, null, null, null, null, DateTimeOffset.UtcNow);

    [Fact]
    public async Task An_applied_mutation_is_recorded_and_returns_Applied()
    {
        var store = new FakeStore();
        var task = NewTask("t_01ARZ3NDEKTSV4RRFFQ69G5FAV", "Water the plants");

        var result = await store.MutateAsync<Never>(_ => new StoreMutation([new TasksWrite([task])]), CancellationToken.None);

        Assert.True(result.IsT0);
        Assert.IsType<Applied>(result.AsT0);
        var recordedWrite = Assert.IsType<TasksWrite>(Assert.Single(Assert.Single(store.Mutations).OrderedWrites));
        Assert.Same(task, Assert.Single(recordedWrite.Tasks));
    }

    [Fact]
    public async Task An_applied_write_is_visible_to_the_next_Read()
    {
        var store = new FakeStore();
        var task = NewTask("t_01ARZ3NDEKTSV4RRFFQ69G5FAV", "Water the plants");

        await store.MutateAsync<Never>(_ => new StoreMutation([new TasksWrite([task])]), CancellationToken.None);

        Assert.Same(task, Assert.Single(store.Read().Tasks));
    }

    [Fact]
    public async Task The_mutation_lambda_is_handed_the_view_as_it_stands_at_call_time()
    {
        var store = new FakeStore();
        var first = NewTask("t_01ARZ3NDEKTSV4RRFFQ69G5FA1", "First");
        var second = NewTask("t_01ARZ3NDEKTSV4RRFFQ69G5FA2", "Second");

        await store.MutateAsync<Never>(_ => new StoreMutation([new TasksWrite([first])]), CancellationToken.None);
        await store.MutateAsync<Never>(view => new StoreMutation([new TasksWrite([.. view.Tasks, second])]), CancellationToken.None);

        Assert.Equal([first, second], store.Read().Tasks);
    }

    [Fact]
    public async Task A_refused_mutation_returns_the_refusal_writes_nothing_and_is_not_recorded()
    {
        var store = new FakeStore();

        var result = await store.MutateAsync(_ => OneOf<StoreMutation, string>.FromT1("refused: nothing to do"), CancellationToken.None);

        Assert.True(result.IsT1);
        Assert.Equal("refused: nothing to do", result.AsT1);
        Assert.Empty(store.Mutations);
        Assert.Empty(store.Read().Tasks);
        Assert.Equal(1, store.RefusalCount);
    }

    [Fact]
    public async Task LastWriteSucceeded_is_null_before_any_write_and_true_after_one()
    {
        var store = new FakeStore();
        Assert.Null(store.LastWriteSucceeded);

        await store.MutateAsync<Never>(_ => new StoreMutation([new TasksWrite([])]), CancellationToken.None);

        Assert.True(store.LastWriteSucceeded);
    }

    [Fact]
    public async Task LastWriteSucceeded_is_untouched_by_a_refusal_and_by_an_empty_write_list()
    {
        var store = new FakeStore();

        await store.MutateAsync(_ => OneOf<StoreMutation, string>.FromT1("refused"), CancellationToken.None);
        Assert.Null(store.LastWriteSucceeded);

        await store.MutateAsync<Never>(_ => new StoreMutation([]), CancellationToken.None);
        Assert.Null(store.LastWriteSucceeded);
    }

    private sealed record UnrecognisedWrite;

    [Fact]
    public async Task An_unrecognised_write_payload_throws_naming_its_type()
    {
        var store = new FakeStore();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => store.MutateAsync<Never>(
            _ => new StoreMutation([new UnrecognisedWrite()]), CancellationToken.None));

        Assert.Contains(nameof(UnrecognisedWrite), exception.Message);
    }

    [Fact]
    public async Task A_written_fire_row_survives_the_next_unrelated_mutation()
    {
        var date = new DateOnly(2026, 8, 15);
        var row = new FireRow(null, FireKind.Fallback, null, null, null, null, DateTimeOffset.UtcNow, null, null);
        var store = new FakeStore();

        await store.MutateAsync<Never>(_ => new StoreMutation([new FiresWrite(new DayFires(date, [row]))]), CancellationToken.None);
        await store.MutateAsync<Never>(_ => new StoreMutation([new TasksWrite([])]), CancellationToken.None);

        Assert.Same(row, Assert.Single(store.Read().FiresOn(date).Rows));
    }

    [Fact]
    public async Task A_completion_log_seeded_for_a_task_absent_from_Tasks_survives_the_next_mutation()
    {
        var taskId = new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5FAV");
        var entry = new CompletionEntry(new DateOnly(2026, 8, 15), DateTimeOffset.UtcNow);
        var log = CompletionLog.Empty(taskId).With(entry);
        var store = new FakeStore();

        await store.MutateAsync<Never>(_ => new StoreMutation([new CompletionLogWrite(log)]), CancellationToken.None);
        await store.MutateAsync<Never>(_ => new StoreMutation([new TasksWrite([])]), CancellationToken.None);

        Assert.Equal(entry, Assert.Single(store.Read().CompletionsFor(taskId).Entries));
    }

    [Fact]
    public async Task A_view_already_built_is_unaffected_by_a_later_With_call_on_the_same_builder()
    {
        var taskId = new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5FAV");
        var otherTaskId = new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5FA2");
        var log = CompletionLog.Empty(taskId).With(new CompletionEntry(new DateOnly(2026, 8, 15), DateTimeOffset.UtcNow));
        var builder = new FakeStoreViewBuilder().WithCompletions(taskId, log);

        var view = builder.Build();
        builder.WithCompletions(otherTaskId, CompletionLog.Empty(otherTaskId).With(new CompletionEntry(null, DateTimeOffset.UtcNow)));

        Assert.Empty(view.CompletionsFor(otherTaskId).Entries);
    }
}
