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
        bool defaultPairIntact,
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
        DefaultPairIntact = defaultPairIntact;
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

    /// <summary>Set by <see cref="FakeStoreViewBuilder.Build"/> to <c>true</c> only when
    /// <em>neither</em> <see cref="DayTemplates"/> nor <see cref="Patterns"/> has ever been
    /// caller-supplied — the builder still owns both halves of its synthetic default pair. <see
    /// cref="FakeStore.ViewAsBuilder"/> reads this to decide whether to skip replaying both
    /// <c>WithDayTemplates</c> and <c>WithPatterns</c> onto a fresh builder, so the pair keeps
    /// re-deriving itself, or to replay both — because once either half is caller-supplied, every
    /// write must behave exactly like <c>JsonStore</c>: no fix-up, orphans surface (#116 finding 1).</summary>
    internal bool DefaultPairIntact { get; }

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
