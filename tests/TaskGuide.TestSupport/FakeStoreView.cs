using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tasks;

namespace TaskGuide.TestSupport;

/// <summary>
/// An <see cref="IStoreView"/> that never throws — the replacement for the private,
/// mostly-throwing fakes each lane used to hand-roll. Most members default to empty;
/// <see cref="DayTemplates"/> and <see cref="Patterns"/> default instead to one vanilla Day
/// template and a Pattern whose seven weekday slots all name it, because <see
/// cref="PatternBook.Active"/> throws on an active id matching no Pattern and the central read
/// path (<c>DayShapeReader.For</c>) opens by reading it — an "empty" default there would still
/// throw. Built through <see cref="FakeStoreViewBuilder"/>.
/// </summary>
public sealed class FakeStoreView : IStoreView
{
    private readonly IReadOnlyDictionary<TaskId, CompletionLog> _completions;
    private readonly PatternBook _patterns;
    private readonly IReadOnlyDictionary<DateOnly, DayFires> _fires;

    internal FakeStoreView(
        IReadOnlyList<TaskItem> tasks,
        IReadOnlyDictionary<TaskId, CompletionLog> completions,
        IReadOnlyList<DerivedCompletionEntry> derivedCompletions,
        IReadOnlyList<DayTemplate> dayTemplates,
        PatternBook patterns,
        IReadOnlyList<DateOverride> overrides,
        IReadOnlyList<Event> events,
        IReadOnlyList<EventException> eventExceptions,
        IReadOnlyDictionary<DateOnly, DayFires> fires)
    {
        Tasks = tasks;
        _completions = completions;
        DerivedCompletions = derivedCompletions;
        DayTemplates = dayTemplates;
        _patterns = patterns;
        Overrides = overrides;
        Events = events;
        EventExceptions = eventExceptions;
        _fires = fires;
    }

    public IReadOnlyList<TaskItem> Tasks { get; }
    public IReadOnlyList<DerivedCompletionEntry> DerivedCompletions { get; }
    public IReadOnlyList<DayTemplate> DayTemplates { get; }
    public IReadOnlyList<DateOverride> Overrides { get; }
    public IReadOnlyList<Event> Events { get; }
    public IReadOnlyList<EventException> EventExceptions { get; }

    public PatternBook Patterns => _patterns;

    /// <summary>Everything seeded via <c>WithCompletions</c>/<c>CompletionLogWrite</c>, for
    /// <see cref="FakeStore"/> to carry forward across a mutation without walking <see cref="Tasks"/>.</summary>
    internal IReadOnlyDictionary<TaskId, CompletionLog> AllCompletions => _completions;

    /// <summary>Everything seeded via <c>WithFires</c>/<c>FiresWrite</c>, for the same reason as
    /// <see cref="AllCompletions"/>.</summary>
    internal IReadOnlyDictionary<DateOnly, DayFires> AllFires => _fires;

    public CompletionLog CompletionsFor(TaskId task) =>
        _completions.TryGetValue(task, out var log) ? log : CompletionLog.Empty(task);

    public DayFires FiresOn(DateOnly date) =>
        _fires.TryGetValue(date, out var fires) ? fires : new DayFires(date, []);
}
