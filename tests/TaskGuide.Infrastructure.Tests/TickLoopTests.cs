using TaskGuide.Domain.Common;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.Infrastructure.BackgroundServices;
using TaskGuide.Infrastructure.Health;
using TaskGuide.TestSupport;
using Xunit;

namespace TaskGuide.Infrastructure.Tests;

/// <summary>
/// The walking skeleton's proof-of-life tick (#51 step D): every tick records health freshness;
/// exactly one Pushover push happens across the process's lifetime, proving the capture → push
/// pipeline once, not every 30 seconds.
/// </summary>
public sealed class TickLoopTests : IDisposable
{
    private readonly string _dataDir = Directory.CreateTempSubdirectory("taskguide-tickloop-tests-").FullName;

    public void Dispose() => Directory.Delete(_dataDir, recursive: true);

    private static TaskItem NewTask(string title) => new(
        new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5FAV"), title, null, TagSet.Empty, null, null, null, null, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Every_tick_records_health_freshness_even_with_nothing_to_push()
    {
        var store = new FakeStore();
        var pushover = new RecordingReceiptSender();
        var heartbeat = new TickHeartbeat();
        var health = new HealthReporter(store, heartbeat, _dataDir);
        var loop = new TickLoop(store, pushover, heartbeat);

        await loop.TickOnceAsync(CancellationToken.None);

        Assert.NotNull(health.Current().LastTick);
        Assert.Empty(pushover.Receipts);
    }

    [Fact]
    public async Task Sends_exactly_one_push_across_many_ticks_once_a_task_exists()
    {
        var store = new FakeStore(new FakeStoreViewBuilder().WithTasks([NewTask("Fix the shelf bracket")]).Build());
        var pushover = new RecordingReceiptSender();
        var heartbeat = new TickHeartbeat();
        var loop = new TickLoop(store, pushover, heartbeat);

        for (var i = 0; i < 5; i++)
        {
            await loop.TickOnceAsync(CancellationToken.None);
        }

        Assert.Single(pushover.Receipts);
        Assert.Equal("Fix the shelf bracket", pushover.Receipts[0].TaskTitle);
    }
}
