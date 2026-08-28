using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Infrastructure.Storage;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>DI wiring for the walking skeleton's storage substrate (#51).</summary>
public static class JsonStoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IStore"/> backed by <paramref name="dataDir"/>. The whole store loads
    /// into memory at registration time, per <see cref="IStore"/>'s memory-authoritative contract.
    /// </summary>
    public static IServiceCollection AddJsonStore(this IServiceCollection services, string dataDir)
    {
        // Eager, not a factory: constructing here — at the `AddJsonStore` call in Program.cs,
        // before the host is even built — is what makes "loads at registration time" true. A
        // registered factory would defer construction (and so `Load`) to first resolution,
        // which would surface a corrupt or unreadable tasks.json on the first request instead
        // of refusing to start, contradicting the skeleton's startup sequence (assert →
        // snapshot → migrate → sweep → serve).
        services.AddSingleton<IStore>(new JsonStore(dataDir));
        return services;
    }

    /// <summary>
    /// Registers <see cref="IStartupSequence"/> backed by <paramref name="dataDir"/>, wired to
    /// <see cref="KnownDimensions.Default"/> and <see cref="StoreMigrations.Ordered"/>.
    /// </summary>
    /// <remarks>
    /// Registration only — nothing here calls <see cref="StartupSequence.RunAsync"/>. There is a
    /// known ordering defect in the composition root: <see cref="AddJsonStore"/> constructs
    /// <see cref="JsonStore"/> (and so its <c>Load</c>) eagerly, before any startup sequence could
    /// run, so a migration would need to happen before the store that needs migrating is even
    /// loaded. <see cref="StoreMigrations.Ordered"/> is empty today, so the defect is latent —
    /// recorded against the branch's final triage, not fixed here.
    /// <para>
    /// <paramref name="signalRegistryCollision"/> carries the outbound alert a Dimension registry
    /// collision fires before the process exits (per Liveness); the walking skeleton has nothing
    /// to plug in yet, so callers that don't care can pass a no-op.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddStartupSequence(
        this IServiceCollection services,
        string dataDir,
        Func<string, CancellationToken, Task> signalRegistryCollision)
    {
        services.AddSingleton<IStartupSequence>(sp => new StartupSequence(
            sp.GetRequiredService<IStore>(),
            dataDir,
            KnownDimensions.Default,
            new SnapshotWriter(dataDir),
            StoreMigrations.Ordered,
            () => DateTimeOffset.UtcNow,
            signalRegistryCollision,
            sp.GetRequiredService<IIdMinter>()));
        return services;
    }
}
