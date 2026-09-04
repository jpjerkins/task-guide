using TaskGuide.Domain.Common;
using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tasks;

namespace TaskGuide.TestSupport;

/// <summary>
/// Builds a <see cref="FakeStoreView"/>. Mutable-then-build: each <c>With…</c> call records onto
/// the same builder instance and returns it, and <see cref="Build"/> snapshots the recorded state
/// into an immutable view.
/// </summary>
public sealed class FakeStoreViewBuilder
{
    private IReadOnlyList<TaskItem> _tasks = [];
    private readonly Dictionary<TaskId, CompletionLog> _completions = [];
    private IReadOnlyList<DerivedCompletionEntry> _derivedCompletions = [];
    private IReadOnlyList<DayTemplate> _dayTemplates = [];
    private PatternBook _patterns = new(new PatternId(""), []);
    private IReadOnlyList<DateOverride> _overrides = [];
    private IReadOnlyList<Event> _events = [];
    private IReadOnlyList<EventException> _eventExceptions = [];
    private readonly Dictionary<DateOnly, DayFires> _fires = [];

    public FakeStoreViewBuilder WithTasks(IEnumerable<TaskItem> tasks)
    {
        _tasks = [.. tasks];
        return this;
    }

    public FakeStoreViewBuilder WithDayTemplates(IEnumerable<DayTemplate> templates)
    {
        _dayTemplates = [.. templates];
        return this;
    }

    public FakeStoreViewBuilder WithPatterns(PatternBook patterns)
    {
        _patterns = patterns;
        return this;
    }

    public FakeStoreViewBuilder WithOverrides(IEnumerable<DateOverride> overrides)
    {
        _overrides = [.. overrides];
        return this;
    }

    public FakeStoreViewBuilder WithEvents(IEnumerable<Event> events)
    {
        _events = [.. events];
        return this;
    }

    public FakeStoreViewBuilder WithEventExceptions(IEnumerable<EventException> exceptions)
    {
        _eventExceptions = [.. exceptions];
        return this;
    }

    public FakeStoreViewBuilder WithDerivedCompletions(IEnumerable<DerivedCompletionEntry> entries)
    {
        _derivedCompletions = [.. entries];
        return this;
    }

    public FakeStoreViewBuilder WithCompletions(TaskId task, CompletionLog log)
    {
        _completions[task] = log;
        return this;
    }

    public FakeStoreViewBuilder WithFires(DateOnly date, DayFires fires)
    {
        _fires[date] = fires;
        return this;
    }

    /// <summary>Bulk-seeds completions, for <see cref="FakeStore"/> carrying a whole view forward.</summary>
    internal FakeStoreViewBuilder WithAllCompletions(IReadOnlyDictionary<TaskId, CompletionLog> completions)
    {
        foreach (var (task, log) in completions) _completions[task] = log;
        return this;
    }

    /// <summary>Bulk-seeds fires, for the same reason as <see cref="WithAllCompletions"/>.</summary>
    internal FakeStoreViewBuilder WithAllFires(IReadOnlyDictionary<DateOnly, DayFires> fires)
    {
        foreach (var (date, dayFires) in fires) _fires[date] = dayFires;
        return this;
    }

    public FakeStoreView Build() => new(
        _tasks, new Dictionary<TaskId, CompletionLog>(_completions), _derivedCompletions, _dayTemplates, _patterns,
        _overrides, _events, _eventExceptions, new Dictionary<DateOnly, DayFires>(_fires));
}
