using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tasks;

namespace TaskGuide.Application.Ports;

// One record per file kind a mutation can touch. `StoreMutation.OrderedWrites` carries these, in
// the order they must hit disk — see IStore.MutateAsync for the write-order rule.
public sealed record TasksWrite(IReadOnlyList<TaskItem> Tasks);
public sealed record DayTemplatesWrite(IReadOnlyList<DayTemplate> Templates);
public sealed record PatternsWrite(PatternBook Book);
public sealed record OverridesWrite(IReadOnlyList<DateOverride> Overrides);
public sealed record EventsWrite(IReadOnlyList<Event> Events);
public sealed record EventExceptionsWrite(IReadOnlyList<EventException> Exceptions);
public sealed record CompletionLogWrite(CompletionLog Log);
public sealed record DerivedCompletionsWrite(IReadOnlyList<DerivedCompletionEntry> Entries);
public sealed record FiresWrite(DayFires Fires);
