using TaskGuide.Domain.Common;
using TaskGuide.Domain.Tags;

namespace TaskGuide.Domain.Tasks;

/// <summary>
/// A thing to do. `CONTEXT.md` calls this a <b>Task</b>; the type is <c>TaskItem</c> only
/// because <c>Task</c> is <c>System.Threading.Tasks.Task</c> and shadowing it in a project that
/// also does async is a trap. This is the one place the ubiquitous language and the code differ,
/// and the rename is confined to the type name — never to a field, an endpoint or the UI.
/// </summary>
/// <remarks>
/// <b>There is no Status field, and its absence is the decision (#47.)</b> Status is a derived
/// label read off these properties plus the completion log. Exactly one fact behind it is
/// authored — completion — and it lives in <see cref="CompletionLog"/>, not here.
/// <para>
/// Duration is not a field either: it is an ordinal Dimension living in <see cref="Tags"/>,
/// which keeps the startup registry sweep uniform across every axis.
/// </para>
/// </remarks>
public sealed record TaskItem(
    TaskId Id,
    string Title,
    string? Notes,
    TagSet Tags,
    DateOnly? Deadline,
    Defer? Defer,
    DateOnly? Postpone,
    Recurrence? Recurrence,
    DateTimeOffset CreatedAt)
{
    /// <summary>
    /// A derived Task is a projection of a trigger through a rule: read-only, no lifecycle,
    /// nothing stored. Marking it done is the only interaction.
    /// </summary>
    public DerivedProvenance? Provenance { get; init; }
}

/// <summary>
/// Why a derived Task is here and where to go to change it — the same debuggability concern
/// that put the read-only dimensions viewer in the UI inventory.
/// </summary>
public sealed record DerivedProvenance(RuleId RuleId, string TriggerId);
