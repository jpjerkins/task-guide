using Microsoft.Extensions.Configuration;
using TaskGuide.Application.Ports;
using TaskGuide.Infrastructure.Pushover;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>DI wiring for the walking skeleton's Pushover integration (#51, #3).</summary>
public static class PushoverServiceCollectionExtensions
{
    /// <summary>
    /// Registers one shared <see cref="PushoverClient"/> behind three driven-port faces (#69:
    /// three callers, three failure contracts, three lifetimes). A singleton is safe here because
    /// <see cref="PushoverClient"/> holds only an <see cref="IHttpClientFactory"/> and creates a
    /// client per send (#76) — it never pins an <see cref="HttpMessageHandler"/>, so
    /// <c>IHttpClientFactory</c>'s handler rotation keeps working even for a process-lifetime
    /// consumer like <c>TickLoop</c>, which is registered as an <c>IHostedService</c> singleton
    /// and would otherwise captive one client's handler for the life of the process.
    /// </summary>
    public static IServiceCollection AddPushover(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PushoverOptions>(configuration.GetSection(PushoverOptions.SectionName));
        services.AddHttpClient(PushoverClient.HttpClientName);
        services.AddSingleton<PushoverClient>();
        services.AddSingleton<IReminderSender>(sp => sp.GetRequiredService<PushoverClient>());
        services.AddSingleton<IReceiptSender>(sp => sp.GetRequiredService<PushoverClient>());
        services.AddSingleton<IGlanceSender>(sp => sp.GetRequiredService<PushoverClient>());
        return services;
    }
}
