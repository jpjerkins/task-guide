using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;

namespace TaskGuide.Infrastructure.Storage;

/// <summary>
/// ADR-0009's composition root: plan (may refuse, cannot write) → apply (cannot refuse) → open the
/// runtime store. The one place these three phases are wired together.
/// </summary>
public static class StartupBootstrap
{
    /// <summary>
    /// Constructs a bootstrap <see cref="JsonStore"/> — never registered in DI, never kept alive
    /// past this call — plans against it as an <see cref="IStoreReader"/>, applies the plan to
    /// that same instance if it isn't a refusal, then opens and returns a <b>fresh</b>
    /// <see cref="JsonStore"/> over <paramref name="dataDir"/> whose read view loads from what the
    /// write phase just landed. The bootstrap instance goes out of scope here: <see cref="JsonStore"/>
    /// holds no unmanaged handle and is not <see cref="IDisposable"/>, so there is nothing to
    /// release.
    /// </summary>
    public static async Task<IStore> BootstrapAndOpenStoreAsync(
        string dataDir,
        DimensionRegistry registry,
        IReadOnlyList<StoreMigration> migrations,
        IIdMinter idMinter,
        TimeProvider clock,
        Func<string, CancellationToken, Task> signalRegistryCollision,
        CancellationToken cancellationToken)
    {
        var bootstrap = new JsonStore(dataDir);
        var planner = new StartupPlanner(dataDir, registry, migrations, idMinter);

        // IStoreReader, not IStore: the parameter type is the whole enforcement that the planner
        // cannot be handed something that writes.
        var result = planner.Plan(bootstrap);

        if (result.IsT1)
        {
            // Match, not IsT0/AsT0/AsT1: a third refusal kind must break this at compile time
            // (ADR-0011) rather than fall through to the wrong arm at runtime.
            await result.AsT1.Match(
                async collision =>
                {
                    // Reconstructs the exception from the facts RegistryCollision carries (#78) so
                    // its Message is formatted exactly once, by DuplicateDimensionValueException
                    // itself — the operator signal and the thrown exception then say the same
                    // thing, rather than the signal nesting an already-formatted string back into
                    // the formatter's own value parameter.
                    var exception = new DuplicateDimensionValueException(collision.Value, collision.ClaimedBy);
                    await signalRegistryCollision(exception.Message, cancellationToken);
                    throw exception;
                },
                versionAhead => throw new StoreVersionAheadException(versionAhead.StoredVersion, versionAhead.CurrentVersion));
        }

        var writer = new StartupWriter(bootstrap, dataDir, new SnapshotWriter(dataDir), clock);
        await writer.ApplyAsync(result.AsT0, cancellationToken);

        // Fresh, not the bootstrap instance: its read view must load from what the write phase
        // just landed on disk, which the bootstrap instance's own in-memory view (loaded before
        // the write) cannot see.
        return new JsonStore(dataDir);
    }
}
