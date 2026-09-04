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

        var exception = await Assert.ThrowsAsync<NotImplementedException>(() => store.MutateAsync<Never>(
            _ => new StoreMutation([new UnrecognisedWrite()]), CancellationToken.None));

        Assert.Contains(nameof(UnrecognisedWrite), exception.Message);
    }

    [Fact]
    public async Task A_write_that_throws_mid_apply_leaves_Mutations_empty()
    {
        var store = new FakeStore();

        await Assert.ThrowsAsync<NotImplementedException>(() => store.MutateAsync<Never>(
            _ => new StoreMutation([new UnrecognisedWrite()]), CancellationToken.None));

        Assert.Empty(store.Mutations);
    }

    [Fact]
    public async Task A_PatternsWrites_Patterns_list_is_deep_copied_not_stored_by_reference()
    {
        var template = new DayTemplate(new DayTemplateId("dt_workday"), "Workday", [], []);
        var pattern = new Pattern(new PatternId("p_default"), "Default", Enumerable.Repeat(template.Id, 7).ToArray());
        var otherPattern = new Pattern(new PatternId("p_other"), "Other", Enumerable.Repeat(template.Id, 7).ToArray());
        var patternsList = new List<Pattern> { pattern };
        var book = new PatternBook(pattern.Id, patternsList);
        var store = new FakeStore();

        await store.MutateAsync<Never>(_ => new StoreMutation([new PatternsWrite(book)]), CancellationToken.None);
        patternsList.Add(otherPattern);

        Assert.Same(pattern, Assert.Single(store.Read().Patterns.Patterns));
    }

    [Fact]
    public async Task A_CompletionLogWrites_Entries_list_is_deep_copied_not_stored_by_reference()
    {
        var taskId = new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5FAV");
        var entry = new CompletionEntry(new DateOnly(2026, 8, 15), DateTimeOffset.UtcNow);
        var otherEntry = new CompletionEntry(new DateOnly(2026, 8, 16), DateTimeOffset.UtcNow);
        var entries = new List<CompletionEntry> { entry };
        var log = new CompletionLog(taskId, entries);
        var store = new FakeStore();

        await store.MutateAsync<Never>(_ => new StoreMutation([new CompletionLogWrite(log)]), CancellationToken.None);
        entries.Add(otherEntry);

        Assert.Equal(entry, Assert.Single(store.Read().CompletionsFor(taskId).Entries));
    }

    [Fact]
    public async Task A_FiresWrites_Rows_list_is_deep_copied_not_stored_by_reference()
    {
        var date = new DateOnly(2026, 8, 15);
        var row = new FireRow(null, FireKind.Fallback, null, null, null, null, DateTimeOffset.UtcNow, null, null);
        var otherRow = new FireRow(null, FireKind.Fallback, null, null, null, null, DateTimeOffset.UtcNow, null, null);
        var rows = new List<FireRow> { row };
        var store = new FakeStore();

        await store.MutateAsync<Never>(_ => new StoreMutation([new FiresWrite(new DayFires(date, rows))]), CancellationToken.None);
        rows.Add(otherRow);

        Assert.Same(row, Assert.Single(store.Read().FiresOn(date).Rows));
    }

    [Fact]
    public void Concurrent_MutateAsync_calls_serialise_so_none_of_their_writes_are_lost()
    {
        // Dedicated OS threads, not Task.Run/the thread pool: the pool ramps up new threads
        // slowly, which would mask the race this test exists to catch by serialising writers
        // anyway before they overlap. A Barrier lines every thread up so all 32 calls truly
        // race MutateAsync's read-apply-assign at once.
        const int concurrentWriters = 32;
        var store = new FakeStore();
        using var ready = new Barrier(concurrentWriters);

        var threads = Enumerable.Range(0, concurrentWriters).Select(i => new Thread(() =>
        {
            ready.SignalAndWait();
            store.MutateAsync<Never>(
                view => new StoreMutation([new TasksWrite([.. view.Tasks, NewTask($"t_writer_{i:D3}", $"Writer {i}")])]),
                CancellationToken.None).GetAwaiter().GetResult();
        })).ToArray();

        foreach (var thread in threads) thread.Start();
        foreach (var thread in threads) thread.Join();

        Assert.Equal(concurrentWriters, store.Read().Tasks.Count);
    }

    [Fact]
    public async Task MutateAsync_throws_for_an_already_cancelled_token()
    {
        var store = new FakeStore();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => store.MutateAsync<Never>(
            _ => new StoreMutation([]), cts.Token));
    }

    [Fact]
    public async Task FailNextWrite_makes_the_next_write_throw_reports_LastWriteSucceeded_false_and_applies_nothing()
    {
        var store = new FakeStore();
        var task = NewTask("t_01ARZ3NDEKTSV4RRFFQ69G5FAV", "Water the plants");
        store.FailNextWrite();

        await Assert.ThrowsAsync<IOException>(() => store.MutateAsync<Never>(
            _ => new StoreMutation([new TasksWrite([task])]), CancellationToken.None));

        Assert.False(store.LastWriteSucceeded);
        Assert.Empty(store.Mutations);
        Assert.Empty(store.Read().Tasks);
    }

    [Fact]
    public async Task FailNextWrite_only_fails_the_next_write_not_the_one_after_it()
    {
        var store = new FakeStore();
        var task = NewTask("t_01ARZ3NDEKTSV4RRFFQ69G5FAV", "Water the plants");
        store.FailNextWrite();

        await Assert.ThrowsAsync<IOException>(() => store.MutateAsync<Never>(
            _ => new StoreMutation([new TasksWrite([task])]), CancellationToken.None));
        await store.MutateAsync<Never>(_ => new StoreMutation([new TasksWrite([task])]), CancellationToken.None);

        Assert.True(store.LastWriteSucceeded);
        Assert.Same(task, Assert.Single(store.Read().Tasks));
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

    /// <summary>#116 finding 1: once the Pattern book is caller-supplied — by seeding or by a
    /// `PatternsWrite` — a later `DayTemplatesWrite` leaves it exactly as it was, orphan and all,
    /// matching `JsonStore`, which does no fix-up.</summary>
    [Fact]
    public async Task A_DayTemplatesWrite_leaves_a_caller_supplied_Pattern_book_exactly_as_it_was()
    {
        var original = new DayTemplate(new DayTemplateId("dt_original"), "Original day", [], []);
        var pattern = new Pattern(new PatternId("p_mine"), "Mine", Enumerable.Repeat(original.Id, 7).ToArray());
        var book = new PatternBook(pattern.Id, [pattern]);
        var store = new FakeStore();
        await store.MutateAsync<Never>(_ => new StoreMutation([new PatternsWrite(book)]), CancellationToken.None);

        var mine = new DayTemplate(new DayTemplateId("dt_mine"), "My day", [], []);
        await store.MutateAsync<Never>(_ => new StoreMutation([new DayTemplatesWrite([mine])]), CancellationToken.None);

        var active = store.Read().Patterns.Active;
        foreach (DayOfWeek weekday in Enum.GetValues<DayOfWeek>())
        {
            Assert.Equal(original.Id, active[weekday]);
        }
    }

    /// <summary>#116 finding 2: a write that throws part-way through `OrderedWrites` — after an
    /// earlier recognised write already landed — must report `LastWriteSucceeded` false, matching
    /// `JsonStore`.</summary>
    [Fact]
    public async Task A_write_that_throws_part_way_through_OrderedWrites_reports_LastWriteSucceeded_false()
    {
        var store = new FakeStore();
        var mine = new DayTemplate(new DayTemplateId("dt_mine"), "My day", [], []);

        await Assert.ThrowsAsync<NotImplementedException>(() => store.MutateAsync<Never>(
            _ => new StoreMutation([new DayTemplatesWrite([mine]), new UnrecognisedWrite()]), CancellationToken.None));

        Assert.False(store.LastWriteSucceeded);
    }

    /// <summary>#116 finding 2: an unrecognised payload as the very first write never reaches a
    /// real apply, so `LastWriteSucceeded` must stay untouched, matching `JsonStore` — this is
    /// the boundary that keeps the fix a rule rather than a blanket `false`.</summary>
    [Fact]
    public async Task An_unrecognised_payload_as_the_very_first_write_leaves_LastWriteSucceeded_untouched()
    {
        var store = new FakeStore();

        await Assert.ThrowsAsync<NotImplementedException>(() => store.MutateAsync<Never>(
            _ => new StoreMutation([new UnrecognisedWrite()]), CancellationToken.None));

        Assert.Null(store.LastWriteSucceeded);
    }
}
