using TaskGuide.Infrastructure.Storage;
using Xunit;

namespace TaskGuide.Storage.Tests;

/// <summary>
/// Against `tests/TEST-INVENTORY.md`'s "fires older than 30 days are unlinked as whole files".
/// </summary>
public sealed class FireRetentionTests : IDisposable
{
    private readonly string _dataDir = Directory.CreateTempSubdirectory("taskguide-storage-tests-").FullName;

    public void Dispose() => Directory.Delete(_dataDir, recursive: true);

    private string SeedFireFile(DateOnly date)
    {
        var firesDir = Path.Combine(_dataDir, "fires");
        Directory.CreateDirectory(firesDir);

        var path = Path.Combine(firesDir, FireCodec.FileNameFor(date));
        File.WriteAllText(path, "[]");
        return path;
    }

    [Fact]
    public void Fires_older_than_30_days_are_unlinked_as_whole_files()
    {
        var old = SeedFireFile(new DateOnly(2026, 7, 28));
        var retained = SeedFireFile(new DateOnly(2026, 7, 29));

        var result = FireRetention.Sweep(_dataDir, new DateOnly(2026, 8, 28));

        Assert.Single(result.Removed);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(retained));
    }

    [Fact]
    public void A_fire_file_exactly_30_days_old_is_kept_the_boundary_must_not_drift()
    {
        var boundary = SeedFireFile(new DateOnly(2026, 7, 29));

        var result = FireRetention.Sweep(_dataDir, new DateOnly(2026, 8, 28));

        Assert.Empty(result.Removed);
        Assert.True(File.Exists(boundary));
    }

    [Fact]
    public void A_file_in_fires_whose_name_is_not_a_date_is_left_untouched()
    {
        var firesDir = Path.Combine(_dataDir, "fires");
        Directory.CreateDirectory(firesDir);
        var unexpected = Path.Combine(firesDir, "not-a-fire.json");
        File.WriteAllText(unexpected, "this is not json");

        var result = FireRetention.Sweep(_dataDir, new DateOnly(2026, 8, 28));

        Assert.Empty(result.Removed);
        Assert.Empty(result.Failed);
        Assert.True(File.Exists(unexpected));
    }

    [Fact]
    public void The_sweep_on_an_absent_fires_directory_is_a_no_op_not_an_error()
    {
        var result = FireRetention.Sweep(_dataDir, new DateOnly(2026, 8, 28));

        Assert.Empty(result.Removed);
        Assert.Empty(result.Failed);
        Assert.False(Directory.Exists(Path.Combine(_dataDir, "fires")));
    }

    /// <summary>
    /// A per-file delete failure must not abort the sweep, and must surface in the result rather
    /// than propagate — Liveness reads write health off this outcome (`IHealth.cs`), so a silent
    /// exception here would mean no write-health signal at all on a lock or permissions failure,
    /// exactly when something is wrong. Two files are seeded eligible for removal, both under one
    /// write-denied directory, so the assertion that matters — the sweep keeps going past the
    /// <em>first</em> failure rather than aborting — is actually exercised; a single failing file
    /// would leave that untested.
    /// </summary>
    /// <remarks>
    /// <b>Fix round 1 (spec FAIL — Linux):</b> the first version of this test isolated a single
    /// file's delete failure with the BSD/Linux <em>immutable</em> flag (`chflags uchg` /
    /// `chattr +i`), shelled out to via `Process.Start`. That crashed with an unhandled
    /// `InvalidOperationException` on an unprivileged Linux user — `chattr +i` needs
    /// `CAP_LINUX_IMMUTABLE`, which pi5's `philj` account (this project's actual Linux dev
    /// target) does not have — breaking "whole suite green," the one bar that mechanism exists
    /// for. Replaced with denying write on the <em>containing</em> `fires/` directory itself, via
    /// the portable <see cref="File.SetUnixFileMode"/> (no shell-out, no elevated capability:
    /// any owner can `chmod` their own directory). This can no longer isolate one specific file —
    /// every delete in the directory fails — so the test seeds <b>two</b> expired files instead of
    /// one, which is what actually proves "keeps going" (a single-file version couldn't
    /// distinguish "kept going" from "aborted after the only failure"). Verified directly with a
    /// throwaway probe before writing this: two files in a write-denied directory,
    /// `Directory.EnumerateFiles` still lists both (only the directory's <em>write</em> bit is
    /// gone, not read/execute), and `File.Delete` on each threw
    /// <see cref="UnauthorizedAccessException"/> while the directory listing and file contents
    /// survived untouched. Same POSIX-only constraint as the codebase's existing `chmod`-based
    /// tests (<c>WholeStoreTests.MakeUnwritable</c>, <c>TaskEndpointsTests.Chmod</c>) — including
    /// their accepted vacuous-pass-as-root tradeoff, which is strictly preferable to this test's
    /// old failure mode (crashing outright as a non-root user).
    /// </remarks>
    [Fact]
    public void A_per_file_delete_failure_is_recorded_and_the_sweep_keeps_going()
    {
        if (OperatingSystem.IsWindows()) return; // chmod-based directory denial is POSIX-specific.

        var firesDir = Path.Combine(_dataDir, "fires");
        var oldA = SeedFireFile(new DateOnly(2026, 7, 28));
        var oldB = SeedFireFile(new DateOnly(2026, 7, 1));
        var retained = SeedFireFile(new DateOnly(2026, 7, 29));

        DenyDirectoryWrite(firesDir);
        try
        {
            var result = FireRetention.Sweep(_dataDir, new DateOnly(2026, 8, 28));

            Assert.Empty(result.Removed);
            Assert.Equal(
                new[] { new DateOnly(2026, 7, 28), new DateOnly(2026, 7, 1) }.OrderBy(d => d.DayNumber),
                result.Failed.OrderBy(d => d.DayNumber));
            Assert.True(File.Exists(oldA), "a failed delete must leave the file exactly as it was");
            Assert.True(File.Exists(oldB));
            Assert.True(File.Exists(retained));
        }
        finally
        {
            AllowDirectoryWrite(firesDir);
        }
    }

    /// <summary>POSIX-only: r-x for the owner, no write — even the owner cannot delete or create
    /// entries in the directory (root is the sole exception, the same vacuous-pass tradeoff the
    /// codebase already accepts for its other `chmod`-based tests). Restored in a `finally` so
    /// <see cref="Dispose"/>'s recursive delete of <see cref="_dataDir"/> still succeeds even if
    /// an assertion above throws.</summary>
#pragma warning disable CA1416 // SetUnixFileMode is POSIX-only; this whole test returns early on Windows.
    private static void DenyDirectoryWrite(string path) =>
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserExecute);

    private static void AllowDirectoryWrite(string path) =>
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
#pragma warning restore CA1416
}
