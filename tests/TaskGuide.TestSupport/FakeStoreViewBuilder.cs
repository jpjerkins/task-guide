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
    /// <summary>
    /// The vanilla Day template an unseeded view falls back to — mirroring
    /// <c>StartupSequence.SeedDefaultPatternAsync</c>'s shape (one plain template, all seven
    /// weekday slots pointing at it) — so <see cref="PatternBook.Active"/> resolves and
    /// <c>DayShapeReader.For</c> can walk an unseeded <see cref="FakeStoreView"/> end to end
    /// instead of throwing (#77 review finding 1).
    /// </summary>
    private static readonly DayTemplate DefaultDayTemplate = new(new DayTemplateId("dt_default"), "Ordinary day", [], []);

    /// <summary>Id and name of the builder's own Pattern book — <see cref="Build"/> re-derives
    /// its weekday slots to always name the first seeded Day template, so <c>WithDayTemplates</c>
    /// alone never orphans it (#116 finding 1).</summary>
    private static readonly PatternId DefaultPatternId = new("p_default");
    private const string DefaultPatternName = "Default";

    private IReadOnlyList<TaskItem> _tasks = [];
    private readonly Dictionary<TaskId, CompletionLog> _completions = [];
    private IReadOnlyList<DerivedCompletionEntry> _derivedCompletions = [];
    private IReadOnlyList<DayTemplate> _dayTemplates = [DefaultDayTemplate];

    /// <summary><c>null</c> means the builder still owns the default Pattern book and <see
    /// cref="Build"/> must derive one that stays coherent with <see cref="_dayTemplates"/>; a
    /// caller-supplied book (via <see cref="WithPatterns"/>) is held here as-is and never touched
    /// (#116 finding 1).</summary>
    private PatternBook? _patterns;

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

    public FakeStoreView Build()
    {
        var callerSupplied = _patterns is not null;
        var dayTemplates = _dayTemplates;
        var patterns = _patterns;

        if (patterns is null)
        {
            // The builder's own default: never throws for any input, so an empty seed still
            // falls back to the vanilla template rather than leaving the derived Pattern
            // dangling (#116 finding 1).
            if (dayTemplates.Count == 0) dayTemplates = [DefaultDayTemplate];

            var derivedPattern = new Pattern(
                DefaultPatternId, DefaultPatternName, Enumerable.Repeat(dayTemplates[0].Id, 7).ToArray());
            patterns = new PatternBook(derivedPattern.Id, [derivedPattern]);
        }

        return new(
            _tasks, new Dictionary<TaskId, CompletionLog>(_completions), _derivedCompletions, dayTemplates, patterns,
            callerSupplied, _overrides, _events, _eventExceptions, new Dictionary<DateOnly, DayFires>(_fires));
    }
}
