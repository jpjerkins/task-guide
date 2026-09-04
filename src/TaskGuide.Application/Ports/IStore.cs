using OneOf;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tasks;

namespace TaskGuide.Application.Ports;

/// <summary>
/// The read side of the store, extended by <see cref="IStore"/> (ADR-0009 amendment): the
/// bootstrap's pre-serving snapshot satisfies this too, so a planner can be handed something that
/// reads without also being handed something that writes.
/// </summary>
public interface IStoreReader
{
    /// <summary>An immutable read view. Not a Snapshot and not a Backup — it is not a copy of anything durable.</summary>
    IStoreView Read();
}

/// <summary>
/// <b>The store is a set of facts held in memory and mirrored to disk; every file is small,
/// whole, and rewritten atomically.</b> The whole store loads at startup into typed objects,
/// every read is served from memory, and every mutation updates memory and writes the affected
/// file(s) before the request returns.
/// </summary>
/// <remarks>
/// Safe because #5 mandates one container: exactly one writer, so memory can be authoritative
/// without inventing coordination. <b>One global write lock</b> — mutations are tens per day and
/// hold it for sub-milliseconds. <b>Reads take an immutable view and never block</b>; matching
/// runs on every tick and every landing-page load and must not wait on a write.
/// </remarks>
public interface IStore : IStoreReader
{
    /// <summary>
    /// Serialises the mutation and writes every affected file before returning.
    /// <b>Cross-file inconsistency is accepted, not engineered away</b>, under one rule:
    /// <em>write the record whose survival makes the inconsistency detectable, first.</em>
    /// For Event-plus-Override that is the Event — a crash after it lands leaves exactly the
    /// condition the overlap check looks for, so the next read re-offers the prompt and the store
    /// heals itself.
    /// </summary>
    /// <remarks>
    /// <see cref="StoreMutation.OrderedWrites"/> carries one payload per file kind — <see
    /// cref="TasksWrite"/>, <see cref="DayTemplatesWrite"/>, <see cref="PatternsWrite"/>, <see
    /// cref="OverridesWrite"/>, <see cref="EventsWrite"/>, <see cref="EventExceptionsWrite"/>,
    /// <see cref="CompletionLogWrite"/>, <see cref="DerivedCompletionsWrite"/>, and <see
    /// cref="FiresWrite"/> — applied in list order, each atomic on its own. A write that throws
    /// part-way leaves the earlier files written; <see cref="LastWriteSucceeded"/> goes false and
    /// the read view is not swapped.
    /// <para>
    /// <b>Widened to <see cref="OneOf{T0,T1}"/> so <paramref name="mutation"/> can refuse</b>
    /// (#70 decision 2). The gate runs <em>inside</em> the write lock: a gate reading a view
    /// fetched before this call has read a stale view — the refusal decision must happen inside
    /// the lambda, against the view it is handed here, not one read earlier — and a refusal needs
    /// a way out of a function that otherwise only returns <see cref="StoreMutation"/>. Signalling
    /// refusal as an empty <c>StoreMutation([])</c> was rejected: it is the one option that
    /// cannot tell a refusal from "nothing to do". A caller with no refusal arm at all uses
    /// <c>MutateAsync&lt;Never&gt;</c>, returning <c>OneOf&lt;StoreMutation, Never&gt;.FromT0(...)</c>.
    /// </para>
    /// </remarks>
    Task<OneOf<Applied, T>> MutateAsync<T>(Func<IStoreView, OneOf<StoreMutation, T>> mutation, CancellationToken cancellationToken);

    /// <summary>
    /// <b>Observed, not probed</b> (#51/#25) — the outcome of the most recent
    /// <see cref="MutateAsync{T}"/> call's actual disk write: <c>null</c> before any write has been
    /// attempted since this process started (an unwritten store is not evidence of anything
    /// wrong, only an <em>observed</em> failure is), <c>true</c> after the write succeeded,
    /// <c>false</c> after it threw. Never a synthetic write on the health path — Liveness reads
    /// this off work the store was already doing.
    /// </summary>
    bool? LastWriteSucceeded { get; }
}

public interface IStoreView
{
    IReadOnlyList<TaskItem> Tasks { get; }
    CompletionLog CompletionsFor(TaskId task);
    IReadOnlyList<DerivedCompletionEntry> DerivedCompletions { get; }
    IReadOnlyList<DayTemplate> DayTemplates { get; }
    PatternBook Patterns { get; }
    IReadOnlyList<DateOverride> Overrides { get; }
    IReadOnlyList<Event> Events { get; }
    IReadOnlyList<EventException> EventExceptions { get; }
    DayFires FiresOn(DateOnly date);
}

/// <summary>The set of files one mutation touches, in the order they must be written.</summary>
public sealed record StoreMutation(IReadOnlyList<object> OrderedWrites);

/// <summary>The mutation ran and every affected file was written.</summary>
public sealed record Applied;

/// <summary>
/// The refusal arm of a mutation that has none — uninhabited, so <c>OneOf&lt;StoreMutation,
/// Never&gt;</c> can only ever be <see cref="StoreMutation"/>. #70 says the tick executor "passes
/// an arm it never uses"; this makes that structurally true rather than merely conventional.
/// </summary>
public sealed record Never
{
    private Never() { }
}
