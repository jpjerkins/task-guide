using Microsoft.Extensions.Configuration;
using TaskGuide.Application.Ports;
using TaskGuide.Infrastructure.Pushover;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>DI wiring for the walking skeleton's Pushover integration (#51, #3).</summary>
public static class PushoverServiceCollectionExtensions
{
    /// <summary>
    /// Registers the typed <see cref="HttpClient"/> once, on the concrete <see cref="PushoverClient"/>,
    /// and exposes that same instance as all three sender ports it implements — one vendor client,
    /// three driven-port faces (#69: three callers, three failure contracts, three lifetimes).
    /// </summary>
    public static IServiceCollection AddPushover(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PushoverOptions>(configuration.GetSection(PushoverOptions.SectionName));
        services.AddHttpClient<PushoverClient>();
        services.AddSingleton<IReminderSender>(sp => sp.GetRequiredService<PushoverClient>());
        services.AddSingleton<IReceiptSender>(sp => sp.GetRequiredService<PushoverClient>());
        services.AddSingleton<IGlanceSender>(sp => sp.GetRequiredService<PushoverClient>());
        return services;
    }
}
