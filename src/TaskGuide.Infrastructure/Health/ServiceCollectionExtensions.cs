using TaskGuide.Application.Ports;
using TaskGuide.Infrastructure.Health;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>DI wiring for the walking skeleton's health reporting (#51, #25).</summary>
public static class HealthReporterServiceCollectionExtensions
{
    /// <summary>
    /// Registers the concrete <see cref="HealthReporter"/> as a singleton, exposed both as
    /// itself (so <c>TickLoop</c> can record ticks on it) and as <see cref="IHealthReporter"/>
    /// (so the <c>/health</c> endpoint can read it) — the same instance either way.
    /// </summary>
    public static IServiceCollection AddHealthReporter(this IServiceCollection services, string dataDir)
    {
        services.AddSingleton(new HealthReporter(dataDir));
        services.AddSingleton<IHealthReporter>(sp => sp.GetRequiredService<HealthReporter>());
        return services;
    }
}
