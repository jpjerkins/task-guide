using Microsoft.Extensions.Hosting;
using TaskGuide.Application.Firing;
using TaskGuide.Domain.Firing;

namespace TaskGuide.Infrastructure.BackgroundServices;

/// <summary>
/// Hosts the firing loop at its ~30-second cadence. Firing policy stays in Application.
/// </summary>
public sealed class TickLoop(ITickLoop ticks) : BackgroundService
{
    public static readonly TimeSpan Interval = FiringPolicy.TickInterval;

    /// <summary>One pass, exposed so tests can drive it without waiting on the real cadence.</summary>
    public Task TickOnceAsync(CancellationToken cancellationToken) =>
        ticks.TickAsync(DateTimeOffset.UtcNow, cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            await TickOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
