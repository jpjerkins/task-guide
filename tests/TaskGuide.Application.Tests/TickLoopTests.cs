using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Notifications;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.Infrastructure.BackgroundServices;
using TaskGuide.Infrastructure.Health;
using Xunit;

namespace TaskGuide.Application.Tests;

/// <summary>
/// The walking skeleton's proof-of-life tick (#51 step D): every tick records health freshness;
/// exactly one Pushover push happens across the process's lifetime, proving the capture → push
/// pipeline once, not every 30 seconds.
/// </summary>
public sealed class TickLoopTests : IDisposable
{
    private sealed class FakeStore : IStore
    {
        private sealed class View(IReadOnlyList<TaskItem> tasks) : IStoreView
        {
            public IReadOnlyList<TaskItem> Tasks { get; } = tasks;
            public CompletionLog CompletionsFor(TaskId task) => throw new NotImplementedException();
            public IReadOnlyList<DerivedCompletionEntry> DerivedCompletions => throw new NotImplementedException();
            public IReadOnlyList<TaskGuide.Domain.Schedule.DayTemplate> DayTemplates => throw new NotImplementedException();
            public TaskGuide.Domain.Schedule.PatternBook Patterns => throw new NotImplementedException();
            public IReadOnlyList<TaskGuide.Domain.Schedule.DateOverride> Overrides => throw new NotImplementedException();
            public IReadOnlyList<TaskGuide.Domain.Schedule.Event> Events => throw new NotImplementedException();
            public IReadOnlyList<TaskGuide.Domain.Schedule.EventException> EventExceptions => throw new NotImplementedException();
            public TaskGuide.Domain.Firing.DayFires FiresOn(DateOnly date) => throw new NotImplementedException();
        }

        public List<TaskItem> Tasks { get; } = [];

        public IStoreView Read() => new View(Tasks);
        public Task MutateAsync(Func<IStoreView, StoreMutation> mutation, CancellationToken cancellationToken) => throw new NotImplementedException();
        public bool? LastWriteSucceeded => null;
    }

    private sealed class CapturingPushoverClient : IPushoverClient
    {
        public List<Receipt> Receipts { get; } = [];

        public Task<bool> SendReminderAsync(Reminder reminder, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task SendReceiptAsync(Receipt receipt, CancellationToken cancellationToken)
        {
            Receipts.Add(receipt);
            return Task.CompletedTask;
        }

        public Task<bool> SendGlanceAsync(Glance glance, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private readonly string _dataDir = Directory.CreateTempSubdirectory("taskguide-tickloop-tests-").FullName;

    public void Dispose() => Directory.Delete(_dataDir, recursive: true);

    private static TaskItem NewTask(string title) => new(
        new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5FAV"), title, null, TagSet.Empty, null, null, null, null, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Every_tick_records_health_freshness_even_with_nothing_to_push()
    {
        var store = new FakeStore();
        var pushover = new CapturingPushoverClient();
        var health = new HealthReporter(store, _dataDir);
        var loop = new TickLoop(store, pushover, health);

        await loop.TickOnceAsync(CancellationToken.None);

        Assert.NotNull(health.Current().LastTick);
        Assert.Empty(pushover.Receipts);
    }

    [Fact]
    public async Task Sends_exactly_one_push_across_many_ticks_once_a_task_exists()
    {
        var store = new FakeStore();
        var pushover = new CapturingPushoverClient();
        var health = new HealthReporter(store, _dataDir);
        var loop = new TickLoop(store, pushover, health);
        store.Tasks.Add(NewTask("Fix the shelf bracket"));

        for (var i = 0; i < 5; i++)
        {
            await loop.TickOnceAsync(CancellationToken.None);
        }

        Assert.Single(pushover.Receipts);
        Assert.Equal("Fix the shelf bracket", pushover.Receipts[0].TaskTitle);
    }
}
