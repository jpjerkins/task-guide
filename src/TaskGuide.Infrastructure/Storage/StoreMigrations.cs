namespace TaskGuide.Infrastructure.Storage;

/// <summary>
/// The ordered N→N+1 steps <see cref="StartupSequence"/> walks when `manifest.json`'s version is
/// behind <see cref="ManifestCodec.CurrentVersion"/>. Per-file versions were rejected (#23) — the
/// version is store-wide, so a step here operates over the whole <paramref name="dataDir"/>, not
/// one collection.
/// </summary>
public static class StoreMigrations
{
    /// <summary>
    /// Empty today — version 1 is the only version that has existed, so there is nothing to walk.
    /// Do not invent a step to have something to run; the walk logic is exercised in
    /// <c>StartupSequenceTests</c> against a fake list supplied through <see cref="StartupSequence"/>'s
    /// constructor.
    /// </summary>
    public static IReadOnlyList<StoreMigration> Ordered { get; } = [];
}

/// <summary>
/// One N→N+1 step: <paramref name="From"/> must equal the manifest version it runs against,
/// <paramref name="To"/> is the version it leaves the store at. <paramref name="Apply"/> takes
/// the store's <c>dataDir</c> — a step operates over the whole store, per the store-wide version.
/// </summary>
public sealed record StoreMigration(int From, int To, Func<string, CancellationToken, Task> Apply);
