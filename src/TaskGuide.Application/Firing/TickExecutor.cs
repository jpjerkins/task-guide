using Microsoft.Extensions.Logging;
using OneOf.Types;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Notifications;
using TaskGuide.Domain.Time;

namespace TaskGuide.Application.Firing;

/// <summary>Delivers already-planned fire intents and records only accepted pushes.</summary>
public sealed class TickExecutor(
    IStore store,
    IReminderSender reminders,
    IGlanceSender glances,
    ITickHeartbeat heartbeat,
    IFireRetention retention,
    DayBoundary boundary,
    ILogger<TickExecutor> logger)
{
    private readonly IStore _store = store;
    private readonly IReminderSender _reminders = reminders;
    private readonly GlanceScheduling _glanceScheduling = new(glances);
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

            if (plan.Glance is { } glance)
            {
                await DeliverGlanceAsync(glance, now, cancellationToken);
            }

            RunSweep(_boundary.DateOf(now), cancellationToken);
        }
        finally
        {
            _heartbeat.RecordTick(now);
        }
    }

    private async Task DeliverGlanceAsync(GlanceState glance, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            await _glanceScheduling.SendAsync(glance, now, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Glance update failed.");
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
            var result = _retention.Sweep(today);
            if (result.Failed.Count > 0)
            {
                _logger.LogError("Fire-record retention failed for {Count} day files.", result.Failed.Count);
            }
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
