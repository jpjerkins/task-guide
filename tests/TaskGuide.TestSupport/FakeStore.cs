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
    private readonly List<StoreMutation> _mutations = [];
    private FakeStoreView _view;

    public FakeStore(FakeStoreView? initial = null) => _view = initial ?? new FakeStoreViewBuilder().Build();

    public IStoreView Read() => _view;

    public Task<OneOf<Applied, T>> MutateAsync<T>(Func<IStoreView, OneOf<StoreMutation, T>> mutation, CancellationToken cancellationToken)
    {
        var outcome = mutation(_view);

        if (!outcome.IsT0)
        {
            RefusalCount++;
            return Task.FromResult(OneOf<Applied, T>.FromT1(outcome.AsT1));
        }

        var storeMutation = outcome.AsT0;
        _mutations.Add(storeMutation);

        if (storeMutation.OrderedWrites.Count > 0)
        {
            var builder = ViewAsBuilder(_view);
            foreach (var write in storeMutation.OrderedWrites)
            {
                Apply(builder, write);
            }

            _view = builder.Build();
            LastWriteSucceeded = true;
        }

        return Task.FromResult(OneOf<Applied, T>.FromT0(new Applied()));
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
            .WithPatterns(view.Patterns)
            .WithOverrides(view.Overrides)
            .WithEvents(view.Events)
            .WithEventExceptions(view.EventExceptions)
            .WithDerivedCompletions(view.DerivedCompletions);

        foreach (var task in view.Tasks)
        {
            builder.WithCompletions(task.Id, view.CompletionsFor(task.Id));
        }

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
                builder.WithPatterns(w.Book);
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
                builder.WithCompletions(w.Log.TaskId, w.Log);
                break;
            case DerivedCompletionsWrite w:
                builder.WithDerivedCompletions(w.Entries);
                break;
            case FiresWrite w:
                builder.WithFires(w.Fires.Date, w.Fires);
                break;
            default:
                throw new ArgumentException($"Unrecognised write payload type: {write.GetType().Name}", nameof(write));
        }
    }
}
