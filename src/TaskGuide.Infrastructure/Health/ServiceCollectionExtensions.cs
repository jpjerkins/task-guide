using TaskGuide.Application.Ports;
using TaskGuide.Infrastructure.Health;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>DI wiring for the walking skeleton's health reporting (#51, #25).</summary>
public static class HealthReporterServiceCollectionExtensions
{
    /// <summary>
    /// Registers the concrete <see cref="HealthReporter"/> as a singleton, exposed both as
    /// itself (so <c>TickLoop</c> can record ticks on it) and as <see cref="IHealthReporter"/>
    /// (so the <c>/health</c> endpoint can read it) — the same instance either way. Call after
    /// <c>AddJsonStore</c> so <see cref="IStore"/> is already registered. This factory isn't the
    /// deferred-construction problem <c>AddJsonStore</c>'s own remarks warn about:
    /// <see cref="HealthReporter"/>'s constructor does no I/O of its own, it only reaches for the
    /// already-constructed <see cref="IStore"/> singleton.
    /// </summary>
    public static IServiceCollection AddHealthReporter(this IServiceCollection services, string dataDir)
    {
        services.AddSingleton(sp => new HealthReporter(sp.GetRequiredService<IStore>(), dataDir));
        services.AddSingleton<IHealthReporter>(sp => sp.GetRequiredService<HealthReporter>());
        return services;
    }
}
