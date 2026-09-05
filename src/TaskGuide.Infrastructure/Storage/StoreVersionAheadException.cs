namespace TaskGuide.Infrastructure.Storage;

/// <summary>
/// `manifest.json`'s version is ahead of this binary's <see cref="ManifestCodec.CurrentVersion"/>
/// — an older binary was installed over newer data. Refusing rather than guessing is what keeps a
/// rollback from silently down-migrating already-migrated data.
/// </summary>
/// <remarks>
/// Thrown by <see cref="StartupBootstrap.BootstrapAndOpenStoreAsync"/> when
/// <see cref="StartupPlanner.Plan"/> returns a <c>StoreVersionAhead</c> refusal — the plan phase
/// itself only returns the refusal value, per ADR-0009's "raise refusals, write nothing" rule; the
/// orchestrator is what turns it into the exception a caller (or a host's failed startup) sees.
/// </remarks>
public sealed class StoreVersionAheadException(int storedVersion, int currentVersion)
    : Exception(
        $"manifest.json is at version {storedVersion}, ahead of this binary's version {currentVersion}. " +
        "Refusing to start rather than silently down-migrate.")
{
    public int StoredVersion { get; } = storedVersion;
    public int CurrentVersion { get; } = currentVersion;
}
