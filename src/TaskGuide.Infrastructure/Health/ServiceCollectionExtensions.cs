using TaskGuide.Application.Ports;
using TaskGuide.Infrastructure.Health;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>DI wiring for the walking skeleton's health reporting (#51, #25).</summary>
public static class HealthReporterServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="TickHeartbeat"/> as a singleton, exposed both as itself (so
    /// <see cref="HealthReporter"/> can take it concretely — #69: two Infrastructure classes need
    /// no port between them) and as <see cref="ITickHeartbeat"/> (so <c>TickLoop</c>, which is
    /// Application-side, can record ticks on it) — the same instance either way. Registers
    /// <see cref="HealthReporter"/> as <see cref="IHealthReporter"/> only, since nothing outside
    /// this project needs the concrete type any more. Call after <c>AddJsonStore</c> so
    /// <see cref="IStore"/> is already registered. Neither factory is the deferred-construction
    /// problem <c>AddJsonStore</c>'s own remarks warn about: neither constructor does I/O of its
    /// own, they only reach for already-constructed singletons.
    /// </summary>
    public static IServiceCollection AddHealthReporter(this IServiceCollection services, string dataDir)
    {
        services.AddSingleton<TickHeartbeat>();
        services.AddSingleton<ITickHeartbeat>(sp => sp.GetRequiredService<TickHeartbeat>());
        services.AddSingleton<IHealthReporter>(sp =>
            new HealthReporter(sp.GetRequiredService<IStore>(), sp.GetRequiredService<TickHeartbeat>(), dataDir));
        return services;
    }
}
