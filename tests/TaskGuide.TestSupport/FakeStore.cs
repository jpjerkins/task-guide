using OneOf;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tasks;

namespace TaskGuide.TestSupport;

/// <summary>
/// An <see cref="IStore"/> that records and applies. The mutation lambda is invoked against the
/// view held right now, matching the real store's contract of running the gate inside the write
/// lock (<see cref="IStore.MutateAsync{T}"/>'s doc) — a fake that read a stale view would not
/// catch that bug in a caller.
/// </summary>
public sealed class FakeStore : IStore
{
    private readonly Lock _lock = new();
    private readonly List<StoreMutation> _mutations = [];
    private FakeStoreView _view;
    private bool _failNextWrite;

    public FakeStore(FakeStoreView? initial = null) => _view = initial ?? new FakeStoreViewBuilder().Build();

    public IStoreView Read() => _view;

    /// <summary>
    /// Makes the next <see cref="MutateAsync{T}"/> call whose <see
    /// cref="StoreMutation.OrderedWrites"/> is non-empty throw instead of applying — <see
    /// cref="LastWriteSucceeded"/> goes <c>false</c> and nothing is recorded or applied, matching
    /// how <c>JsonStore</c> would surface a mid-write disk failure (#77 review finding 5).
    /// A refusal or an empty write list does not consume the flag, the same way neither moves
    /// <see cref="LastWriteSucceeded"/> on a real write.
    /// </summary>
    public void FailNextWrite() => _failNextWrite = true;

    /// <summary>
    /// Read-apply-assign runs under <see cref="_lock"/> — the real store's contract is one global
    /// write lock (<see cref="IStore"/>'s doc), and two concurrent callers racing this fake
    /// unlocked would let the second silently discard the first's writes (#77 review finding 3).
    /// </summary>
    public Task<OneOf<Applied, T>> MutateAsync<T>(Func<IStoreView, OneOf<StoreMutation, T>> mutation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            var outcome = mutation(_view);

            if (!outcome.IsT0)
            {
                RefusalCount++;
                return Task.FromResult(OneOf<Applied, T>.FromT1(outcome.AsT1));
            }

            var storeMutation = outcome.AsT0;

            if (storeMutation.OrderedWrites.Count > 0)
            {
                if (_failNextWrite)
                {
                    _failNextWrite = false;
                    LastWriteSucceeded = false;
                    throw new IOException("Simulated write failure via FakeStore.FailNextWrite().");
                }

                var builder = ViewAsBuilder(_view);
                foreach (var write in storeMutation.OrderedWrites)
                {
                    Apply(builder, write);
                }

                _view = builder.Build();
                LastWriteSucceeded = true;
            }

            // Recorded only once the writes above have actually applied — a mutation that threw
            // part-way (an unrecognised payload, or FailNextWrite above) must not appear here,
            // matching the property's own doc: "the mutations that were actually applied" (#77
            // review finding 4).
            _mutations.Add(storeMutation);

            return Task.FromResult(OneOf<Applied, T>.FromT0(new Applied()));
        }
    }

    public bool? LastWriteSucceeded { get; private set; }

    /// <summary>The mutations that were actually applied — refusals never reach this list.</summary>
    public IReadOnlyList<StoreMutation> Mutations => _mutations;

    /// <summary>How many <see cref="MutateAsync{T}"/> calls returned the refusal arm — enough to
    /// assert a refusal happened without a write, alongside <see cref="Mutations"/> staying put.</summary>
    public int RefusalCount { get; private set; }

    private static FakeStoreViewBuilder ViewAsBuilder(FakeStoreView view)
    {
        var builder = new FakeStoreViewBuilder()
            .WithTasks(view.Tasks)
            .WithDayTemplates(view.DayTemplates)
            .WithOverrides(view.Overrides)
            .WithEvents(view.Events)
            .WithEventExceptions(view.EventExceptions)
            .WithDerivedCompletions(view.DerivedCompletions)
            .WithAllCompletions(view.AllCompletions)
            .WithAllFires(view.AllFires);

        // Replaying WithPatterns unconditionally would pin a caller-supplied book forever (fine)
        // but would also pin the builder's own derived default at its old day template, orphaning
        // it the moment DayTemplatesWrite replaces DayTemplates underneath it. Only replay when
        // the book actually came from a caller, so the builder's own default keeps re-deriving
        // itself in Build() (#116 finding 1).
        if (view.PatternsAreCallerSupplied) builder.WithPatterns(view.Patterns);

        return builder;
    }

    private static void Apply(FakeStoreViewBuilder builder, object write)
    {
        switch (write)
        {
            case TasksWrite w:
                builder.WithTasks(w.Tasks);
                break;
            case DayTemplatesWrite w:
                builder.WithDayTemplates(w.Templates);
                break;
            case PatternsWrite w:
                // A defensive copy, matching JsonStore.cs: the store must own its storage, and
                // IReadOnlyList<T> is not a promise of immutability — a caller that keeps its own
                // reference and mutates it later must not be able to reach a view any concurrent
                // reader already holds (#77 review finding 2). WithTasks and friends already copy
                // via `[.. tasks]`; PatternsWrite, CompletionLogWrite, and FiresWrite are the
                // three that were stored by reference instead.
                builder.WithPatterns(w.Book with { Patterns = w.Book.Patterns.ToArray() });
                break;
            case OverridesWrite w:
                builder.WithOverrides(w.Overrides);
                break;
            case EventsWrite w:
                builder.WithEvents(w.Events);
                break;
            case EventExceptionsWrite w:
                builder.WithEventExceptions(w.Exceptions);
                break;
            case CompletionLogWrite w:
                builder.WithCompletions(w.Log.TaskId, w.Log with { Entries = w.Log.Entries.ToArray() });
                break;
            case DerivedCompletionsWrite w:
                builder.WithDerivedCompletions(w.Entries);
                break;
            case FiresWrite w:
                builder.WithFires(w.Fires.Date, w.Fires with { Rows = w.Fires.Rows.ToArray() });
                break;
            default:
                // Matches JsonStore.cs's type and message shape for the same programming error
                // (#77 review finding 6).
                throw new NotImplementedException($"FakeStore does not know how to write a {write.GetType().Name}.");
        }
    }
}
