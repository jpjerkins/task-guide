using TaskGuide.Application.Ports;

namespace TaskGuide.Api.Endpoints;

/// <summary>
/// This service's entire obligation to monitoring: <c>{ ok, lastTick, storage, uptime }</c> —
/// the boolean for the two automatic consumers, the components for the human and the monitor.
/// Everything past that boundary belongs to the host-monitoring effort.
/// </summary>
/// <remarks>
/// Outside <c>/api</c> on purpose: it is the container's health check, so a wedged loop is
/// restarted automatically, with no alert and no surface in the app.
/// </remarks>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", (IHealthReporter health) => Results.Ok(health.Current())).WithTags("Health");
        return app;
    }
}
