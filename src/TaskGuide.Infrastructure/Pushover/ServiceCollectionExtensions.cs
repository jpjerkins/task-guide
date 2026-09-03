using Microsoft.Extensions.Configuration;
using TaskGuide.Application.Ports;
using TaskGuide.Infrastructure.Pushover;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>DI wiring for the walking skeleton's Pushover integration (#51, #3).</summary>
public static class PushoverServiceCollectionExtensions
{
    /// <summary>
    /// Registers the typed <see cref="HttpClient"/> on the concrete <see cref="PushoverClient"/>
    /// and resolves that one implementation behind all three sender ports it implements — one
    /// vendor client, three driven-port faces (#69: three callers, three failure contracts, three
    /// lifetimes) — but <b>not as a shared instance</b>: <c>AddHttpClient&lt;T&gt;</c> registers
    /// <see cref="PushoverClient"/> itself as transient, so each port is registered transient too,
    /// to match. Each resolution gets its own <see cref="PushoverClient"/>, which is correct
    /// rather than merely quieter because <see cref="PushoverClient"/> is stateless — it holds
    /// only its injected <see cref="HttpClient"/>, <c>IOptions&lt;PushoverOptions&gt;</c> and
    /// <c>ILogger</c>, no mutable fields — so nothing needs a shared instance today. Registering
    /// the ports as singletons instead would be a captive-dependency bug on top of the
    /// false-sharing one: a singleton would pin one transient client's
    /// <see cref="HttpMessageHandler"/> for the process lifetime, defeating
    /// <c>IHttpClientFactory</c>'s handler rotation.
    /// <para>
    /// If <see cref="PushoverClient"/> ever gains per-instance state, these three registrations
    /// must change together — leaving them transient while wrapping a stateful client in a
    /// singleton would silently reintroduce both defects.
    /// </para>
    /// </summary>
    public static IServiceCollection AddPushover(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PushoverOptions>(configuration.GetSection(PushoverOptions.SectionName));
        services.AddHttpClient<PushoverClient>();
        services.AddTransient<IReminderSender>(sp => sp.GetRequiredService<PushoverClient>());
        services.AddTransient<IReceiptSender>(sp => sp.GetRequiredService<PushoverClient>());
        services.AddTransient<IGlanceSender>(sp => sp.GetRequiredService<PushoverClient>());
        return services;
    }
}
