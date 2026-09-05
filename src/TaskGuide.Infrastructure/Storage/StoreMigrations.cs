namespace TaskGuide.Infrastructure.Storage;

/// <summary>
/// The ordered N→N+1 steps <see cref="StartupPlanner"/> walks when `manifest.json`'s version is
/// behind <see cref="ManifestCodec.CurrentVersion"/>. Per-file versions were rejected (#23) — the
/// version is store-wide, so a step here operates over the whole <paramref name="dataDir"/>, not
/// one collection.
/// </summary>
public static class StoreMigrations
{
    /// <summary>
    /// Empty today — version 1 is the only version that has existed, so there is nothing to walk.
    /// Do not invent a step to have something to run; the walk logic is exercised in
    /// <c>StartupSequenceTests</c> against a fake list supplied through <see cref="StartupPlanner"/>'s
    /// constructor.
    /// </summary>
    public static IReadOnlyList<StoreMigration> Ordered { get; } = [];
}

/// <summary>
/// One N→N+1 step: <see cref="From"/> must equal the manifest version it runs against,
/// <see cref="To"/> is the version it leaves the store at. <see cref="Apply"/> takes the store's
/// <c>dataDir</c> — a step operates over the whole store, per the store-wide version.
/// </summary>
/// <remarks>
/// <b>Moving strictly forward is an invariant of the step, enforced here (ADR-0009).</b> A step
/// that does not advance the version lets <see cref="StartupPlanner"/>'s walk cycle — an infinite
/// hang at startup, which is worse than any exception. That is a property of the step alone, so it
/// is checked where the step is built rather than rediscovered mid-walk: no walk, in production or
/// under a test's fake list, can ever be handed one.
/// <para>
/// Deliberately a class and not a <c>record</c>: a record's copy constructor would let
/// <c>step with { To = … }</c> reach a walk without passing this constructor, which is the one
/// door the invariant has to hold. Nothing here needs value equality either — <see cref="Apply"/>
/// is a delegate, so record equality over it would be reference equality wearing a disguise.
/// </para>
/// </remarks>
public sealed class StoreMigration
{
    public StoreMigration(int from, int to, Func<string, CancellationToken, Task> apply)
    {
        if (to <= from)
        {
            throw new ArgumentException(
                $"Migration step {from}→{to} does not move the store version strictly forward; " +
                "a non-monotonic step would let the startup walk cycle and hang.",
                nameof(to));
        }

        From = from;
        To = to;
        Apply = apply;
    }

    /// <summary>The manifest version this step runs against.</summary>
    public int From { get; }

    /// <summary>The version this step leaves the store at. Always greater than <see cref="From"/>.</summary>
    public int To { get; }

    /// <summary>Applies the step over the whole store, given its <c>dataDir</c>.</summary>
    public Func<string, CancellationToken, Task> Apply { get; }
}
