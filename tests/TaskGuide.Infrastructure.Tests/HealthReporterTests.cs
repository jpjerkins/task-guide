using System.Runtime.InteropServices;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.Infrastructure.Health;
using TaskGuide.Infrastructure.Storage;
using Xunit;

namespace TaskGuide.Infrastructure.Tests;

/// <summary>
/// Liveness (`tests/TEST-INVENTORY.md`): <c>/health</c> reports <c>{ ok, lastTick, storage, uptime }</c>,
/// a stalled loop reports <c>ok: false</c> while HTTP still answers, read health parses the file
/// rather than stat-ing it, and write health is <b>observed, not probed</b> off the store's own
/// last real write — never a hardcoded guess.
/// </summary>
public sealed class HealthReporterTests : IDisposable
{
    private readonly string _dataDir = Directory.CreateTempSubdirectory("taskguide-health-tests-").FullName;

    public void Dispose()
    {
        // Undo any chmod a test applied — Directory.Delete needs write+execute on the dir itself.
        if (!OperatingSystem.IsWindows()) Chmod(_dataDir, 0b111_101_101); // 755
        Directory.Delete(_dataDir, recursive: true);
    }

    private static TaskItem NewTask(string title) => new(
        new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5FAV"), title, null, TagSet.Empty, null, null, null, null, DateTimeOffset.UtcNow);

    [Fact]
    public void Ok_is_true_with_a_fresh_tick_and_a_readable_store()
    {
        var store = new JsonStore(_dataDir);
        var heartbeat = new TickHeartbeat();
        var reporter = new HealthReporter(store, heartbeat, _dataDir);
        heartbeat.RecordTick(DateTimeOffset.UtcNow);

        var report = reporter.Current();

        Assert.True(report.Ok);
        Assert.NotNull(report.LastTick);
        Assert.True(report.Storage.Readable);
    }

    [Fact]
    public void Ok_is_false_before_any_tick_has_been_recorded()
    {
        var store = new JsonStore(_dataDir);
        var heartbeat = new TickHeartbeat();
        var reporter = new HealthReporter(store, heartbeat, _dataDir);

        var report = reporter.Current();

        Assert.Null(report.LastTick);
        Assert.False(report.Ok);
    }

    [Fact]
    public void A_stalled_loop_reports_ok_false_while_the_reporter_itself_still_answers()
    {
        var store = new JsonStore(_dataDir);
        var heartbeat = new TickHeartbeat();
        var reporter = new HealthReporter(store, heartbeat, _dataDir);
        heartbeat.RecordTick(DateTimeOffset.UtcNow - HealthReporter.StalenessThreshold - TimeSpan.FromSeconds(1));

        var report = reporter.Current();

        Assert.False(report.Ok);
    }

    [Fact]
    public void Read_health_parses_the_file_a_truncated_tasks_json_reports_unreadable()
    {
        // Boot clean, then corrupt the file on disk — the running JsonStore keeps serving its
        // cached in-memory view regardless (memory-authoritative), but HealthReporter's own
        // re-parse must still catch the corruption, independent of the store ever noticing.
        var store = new JsonStore(_dataDir);
        var heartbeat = new TickHeartbeat();
        var reporter = new HealthReporter(store, heartbeat, _dataDir);
        File.WriteAllText(Path.Combine(_dataDir, "tasks.json"), "{ not valid json, truncated mid-w");
        heartbeat.RecordTick(DateTimeOffset.UtcNow);

        var report = reporter.Current();

        Assert.False(report.Storage.Readable);
        Assert.False(report.Ok); // an unreadable store must also fail the overall predicate
    }

    [Fact]
    public void Writable_is_null_and_does_not_force_ok_false_before_any_write_has_happened()
    {
        var store = new JsonStore(_dataDir);
        var heartbeat = new TickHeartbeat();
        var reporter = new HealthReporter(store, heartbeat, _dataDir);
        heartbeat.RecordTick(DateTimeOffset.UtcNow);

        var report = reporter.Current();

        Assert.Null(report.Storage.Writable);
        Assert.True(report.Ok); // unknown is not evidence of anything wrong
    }

    /// <summary>
    /// The exact repro reported live against the running API: an unwritable data directory,
    /// a write that fails, and /health telling the truth about it afterward.
    /// </summary>
    [Fact]
    public async Task Writable_is_false_and_ok_is_false_after_an_observed_write_failure()
    {
        if (OperatingSystem.IsWindows())
        {
            // chmod-based write denial isn't meaningful on Windows' ACL model; this repro is
            // POSIX-specific (matches the deployment target, pi5/Linux). Skipped rather than
            // faked — see the class remarks on not weakening a test into a vacuous pass.
            return;
        }

        var store = new JsonStore(_dataDir);
        var heartbeat = new TickHeartbeat();
        var reporter = new HealthReporter(store, heartbeat, _dataDir);
        heartbeat.RecordTick(DateTimeOffset.UtcNow);

        Chmod(_dataDir, 0b101_000_000); // 500: r-x for the owner, no write — even for the owner, not root

        await Assert.ThrowsAnyAsync<Exception>(() =>
            store.MutateAsync<Never>(view => new StoreMutation([new TasksWrite((IReadOnlyList<TaskItem>)[.. view.Tasks, NewTask("should fail")])]), CancellationToken.None));

        var report = reporter.Current();

        Assert.False(report.Storage.Writable);
        Assert.False(report.Ok);
    }

    [DllImport("libc", SetLastError = true, EntryPoint = "chmod")]
    private static extern int chmod(string pathname, int mode);

    private static void Chmod(string path, int mode)
    {
        if (chmod(path, mode) != 0)
        {
            throw new IOException($"chmod({path}, {Convert.ToString(mode, 8)}) failed: {Marshal.GetLastWin32Error()}");
        }
    }
}
