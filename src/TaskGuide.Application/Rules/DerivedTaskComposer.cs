using TaskGuide.Application.Ports;
using TaskGuide.Domain.Rules;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tasks;
using TaskGuide.Domain.Time;

namespace TaskGuide.Application.Rules;

/// <summary>Composes stored Tasks with obligations derived from the current store facts.</summary>
public sealed class DerivedTaskComposer(
    IReadOnlyList<IDerivedObligationRule> rules,
    IDayShapeReader shapes,
    DayBoundary boundary,
    TimeProvider timeProvider)
{
    private readonly IReadOnlyList<IDerivedObligationRule> _rules = rules;
    private readonly IDayShapeReader _shapes = shapes;
    private readonly DayBoundary _boundary = boundary;
    private readonly TimeProvider _timeProvider = timeProvider;

    /// <summary>
    /// Produces the runtime Task set without changing the persisted-task view used by write paths.
    /// </summary>
    public IReadOnlyList<TaskItem> Compose(IStoreView view)
    {
        var context = new DerivedObligationContext(
            _timeProvider.GetUtcNow(),
            view.Events,
            view.Overrides,
            _shapes,
            view.DerivedCompletions,
            _boundary)
        {
            Patterns = view.Patterns,
            DayTemplates = view.DayTemplates,
            EventExceptions = view.EventExceptions,
        };

        return [.. view.Tasks, .. _rules.SelectMany(rule => rule.Derive(context))];
    }
}
