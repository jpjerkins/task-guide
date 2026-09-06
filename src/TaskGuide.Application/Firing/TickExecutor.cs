using Microsoft.Extensions.Logging;
using OneOf.Types;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Time;

namespace TaskGuide.Application.Firing;

/// <summary>Delivers already-planned fire intents and records only accepted pushes.</summary>
public sealed class TickExecutor(
    IStore store,
    IReminderSender reminders,
    ITickHeartbeat heartbeat,
    IFireRetention retention,
    DayBoundary boundary,
    ILogger<TickExecutor> logger)
{
    private readonly IStore _store = store;
    private readonly IReminderSender _reminders = reminders;
    private readonly ITickHeartbeat _heartbeat = heartbeat;
    private readonly IFireRetention _retention = retention;
    private readonly DayBoundary _boundary = boundary;
    private readonly ILogger<TickExecutor> _logger = logger;

    public async Task ExecuteAsync(TickPlan plan, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var intent in plan.Fires)
            {
                await DeliverAsync(intent, now, cancellationToken);
            }

            RunSweep(_boundary.DateOf(now), cancellationToken);
        }
        finally
        {
            _heartbeat.RecordTick(now);
        }
    }

    private async Task DeliverAsync(FireIntent intent, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            if (!await _reminders.SendReminderAsync(intent.Reminder, cancellationToken))
            {
                _logger.LogError("Reminder push was rejected for {Kind}.", intent.Kind.GetType().Name);
                return;
            }

            var date = _boundary.DateOf(now);
            await _store.MutateAsync<Never>(view => new StoreMutation([
                new FiresWrite(view.FiresOn(date) with
                {
                    Rows = [.. view.FiresOn(date).Rows, intent.FireRow with { FiredAt = now }],
                }),
            ]), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Tick delivery failed for {Kind}.", intent.Kind.GetType().Name);
        }
    }

    private void RunSweep(DateOnly today, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _retention.Sweep(today);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Fire-record retention sweep failed.");
        }
    }
}
