using Microsoft.Extensions.Hosting;
using TaskGuide.Application.Firing;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Notifications;

namespace TaskGuide.Infrastructure.BackgroundServices;

/// <summary>
/// The walking skeleton's proof-of-life tick (#51 step D). Every ~30s pass records that the
/// service is alive for <see cref="Health.HealthReporter"/>, and — exactly once for the whole
/// process's lifetime, the first time a Task exists to report — sends one Pushover Receipt.
/// That one send proves capture → store → push end to end without paging anyone every tick.
/// </summary>
/// <remarks>
/// This is deliberately <b>not</b> an implementation of <see cref="ITickLoop"/>: that interface's
/// docs commit to firing due Windows, draining Snoozes, evaluating the fallback push and running
/// the retention sweep — none of which exist yet, because #51 carries no domain logic (no
/// matching, no ranking, no Recurrence). Claiming <c>ITickLoop</c> here would promise a full tick
/// pass this skeleton doesn't do. A later ticket should implement it for real, or fold this into
/// it once that becomes possible.
/// </remarks>
public sealed class TickLoop(IStore store, IReceiptSender receipts, ITickHeartbeat heartbeat) : BackgroundService
{
    public static readonly TimeSpan Interval = FiringPolicy.TickInterval;

    // 0/1 instead of bool so the "attempt exactly once" decision is a single atomic operation —
    // "one attempt, failure logged, never retried" beyond IReceiptSender's own up-to-three is now
    // this caller's policy (#76), so the flag flips the moment the attempt is made, not on success.
    private int _hasAttemptedPush;

    /// <summary>One pass: record the tick, then push at most once, ever. Exposed so tests can
    /// drive it directly instead of waiting on the real ~30s cadence.</summary>
    public async Task TickOnceAsync(CancellationToken cancellationToken)
    {
        heartbeat.RecordTick(DateTimeOffset.UtcNow);

        if (Interlocked.CompareExchange(ref _hasAttemptedPush, 0, 0) == 1) return;

        var tasks = store.Read().Tasks;
        if (tasks.Count == 0) return; // nothing captured yet to confirm; try again next tick

        if (Interlocked.Exchange(ref _hasAttemptedPush, 1) == 1) return; // another tick beat us to it

        var task = tasks[0];
        var duration = task.Tags.SingleOn(KnownDimensions.Duration)?.Value ?? "";
        var receipt = new Receipt(task.Id, task.Title, duration, new Uri("https://task-guide.example.ts.net/"));

        // Fire-and-forget policy is ours now, not IReceiptSender's: one attempt, the result
        // discarded, never retried beyond what SendReceiptAsync already tried internally.
        _ = await receipts.SendReceiptAsync(receipt, cancellationToken);
    }

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
