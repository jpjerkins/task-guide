using TaskGuide.Application.Ports;
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
}
