using Microsoft.Extensions.Configuration;
using TaskGuide.Application.Ports;
using TaskGuide.Infrastructure.Pushover;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>DI wiring for the walking skeleton's Pushover integration (#51, #3).</summary>
public static class PushoverServiceCollectionExtensions
{
    public static IServiceCollection AddPushover(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PushoverOptions>(configuration.GetSection(PushoverOptions.SectionName));
        services.AddHttpClient<IPushoverClient, PushoverClient>();
        return services;
    }
}
