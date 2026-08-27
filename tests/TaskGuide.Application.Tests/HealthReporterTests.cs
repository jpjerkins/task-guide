using TaskGuide.Infrastructure.Health;
using Xunit;

namespace TaskGuide.Application.Tests;

/// <summary>
/// Liveness (`tests/TEST-INVENTORY.md`): <c>/health</c> reports <c>{ ok, lastTick, storage, uptime }</c>,
/// a stalled loop reports <c>ok: false</c> while HTTP still answers, and read health parses the
/// file rather than stat-ing it. Only these three are in scope for the walking skeleton (#51) —
/// write health is documented as coming from the retention sweep's outcome, which does not exist
/// yet, so it is not exercised here (see <see cref="HealthReporter"/>'s remarks).
/// </summary>
public sealed class HealthReporterTests : IDisposable
{
    private readonly string _dataDir = Directory.CreateTempSubdirectory("taskguide-health-tests-").FullName;

    public void Dispose() => Directory.Delete(_dataDir, recursive: true);

    [Fact]
    public void Ok_is_true_with_a_fresh_tick_and_a_readable_store()
    {
        var reporter = new HealthReporter(_dataDir);
        reporter.RecordTick(DateTimeOffset.UtcNow);

        var report = reporter.Current();

        Assert.True(report.Ok);
        Assert.NotNull(report.LastTick);
        Assert.True(report.Storage.Readable);
    }

    [Fact]
    public void Ok_is_false_before_any_tick_has_been_recorded()
    {
        var reporter = new HealthReporter(_dataDir);

        var report = reporter.Current();

        Assert.Null(report.LastTick);
        Assert.False(report.Ok);
    }

    [Fact]
    public void A_stalled_loop_reports_ok_false_while_the_reporter_itself_still_answers()
    {
        var reporter = new HealthReporter(_dataDir);
        reporter.RecordTick(DateTimeOffset.UtcNow - HealthReporter.StalenessThreshold - TimeSpan.FromSeconds(1));

        var report = reporter.Current();

        Assert.False(report.Ok);
    }

    [Fact]
    public void Read_health_parses_the_file_a_truncated_tasks_json_reports_unreadable()
    {
        File.WriteAllText(Path.Combine(_dataDir, "tasks.json"), "{ not valid json, truncated mid-w");
        var reporter = new HealthReporter(_dataDir);
        reporter.RecordTick(DateTimeOffset.UtcNow);

        var report = reporter.Current();

        Assert.False(report.Storage.Readable);
        Assert.False(report.Ok); // an unreadable store must also fail the overall predicate
    }
}
