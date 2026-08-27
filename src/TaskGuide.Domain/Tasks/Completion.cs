using TaskGuide.Domain.Common;

namespace TaskGuide.Domain.Tasks;

/// <summary>
/// The only authored completion fact in the model. Every Task has a log, not only a recurring
/// one — a one-off Task's holds at most one entry, and that entry is what makes it `Done`.
/// Kept in full: facts stored, everything else derived.
/// </summary>
/// <param name="Due">The instance satisfied. A one-off Task carries its Deadline, or null.</param>
/// <param name="Done">A recorded fact, therefore an instant.</param>
public sealed record CompletionEntry(DateOnly? Due, DateTimeOffset Done);

/// <summary>
/// Split out of the Task record on purpose (#23): a daily Task is ~3,650 entries a decade, and
/// inlining it would make every title edit rewrite every completion ever logged.
/// </summary>
public sealed record CompletionLog(TaskId TaskId, IReadOnlyList<CompletionEntry> Entries)
{
    public static CompletionLog Empty(TaskId id) => new(id, Array.Empty<CompletionEntry>());
}

/// <summary>
/// A derived obligation's completion. Keyed on `due`, which earns a case for free: complete
/// the obligation, move the tournament, and the logged `due` no longer matches the live one —
/// so the obligation correctly re-derives with nothing having to notice the move.
/// </summary>
public sealed record DerivedCompletionEntry(RuleId RuleId, string TriggerId, DateOnly Due, DateTimeOffset Done);
