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
    /// exactly when something is wrong.
    /// </summary>
    /// <remarks>
    /// <b>Why not the directory-occupation trick Task 7 uses (`WholeStoreTests.MakeUnwritable`):</b>
    /// that works for <c>WriteAtomicAsync</c>'s rename-over-destination, but <c>Sweep</c> calls
    /// <c>Directory.EnumerateFiles</c>, which — confirmed empirically — never yields a path that is
    /// itself a directory; occupying a fire file's path with a directory would make the sweep skip
    /// it entirely rather than fail to delete it. A `chmod` on the file, or on `fires/` itself, was
    /// tried next and also rejected: POSIX `unlink()` checks the <em>containing directory's</em>
    /// write permission, not the target file's own bits, so a single file inside a shared directory
    /// cannot be isolated that way, and denying the directory would block every file in it, not
    /// just one.
    /// <para>
    /// What isolates exactly one file: <c>chflags uchg</c> (macOS) / <c>chattr +i</c> (Linux) — the
    /// BSD/Linux <em>immutable</em> flag lives on the file's own inode, not on the directory, so it
    /// blocks only that file's own delete/rename/write. Verified directly (see the report) before
    /// writing this test: with one file flagged immutable and a sibling plain in the same
    /// directory, `File.Delete` threw <see cref="UnauthorizedAccessException"/> on the flagged file
    /// and succeeded on the sibling. POSIX-only (skipped on Windows, which has no equivalent flag);
    /// the flag is cleared in a <c>finally</c> so <see cref="Dispose"/>'s recursive delete of
    /// <see cref="_dataDir"/> still succeeds even if an assertion above throws.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_per_file_delete_failure_is_recorded_and_the_sweep_keeps_going()
    {
        if (OperatingSystem.IsWindows()) return; // no immutable-file equivalent to isolate one delete.

        var old = new DateOnly(2026, 7, 28);
        var alsoOld = new DateOnly(2026, 7, 1);
        var retained = SeedFireFile(new DateOnly(2026, 7, 29));
        var alsoOldPath = SeedFireFile(alsoOld);
        var undeletablePath = SeedFireFile(old);

        SetImmutable(undeletablePath, true);
        try
        {
            var result = FireRetention.Sweep(_dataDir, new DateOnly(2026, 8, 28));

            Assert.Equal([alsoOld], result.Removed);
            Assert.Equal([old], result.Failed);
            Assert.True(File.Exists(undeletablePath), "the flagged file itself must survive the failed delete");
            Assert.False(File.Exists(alsoOldPath));
            Assert.True(File.Exists(retained));
        }
        finally
        {
            SetImmutable(undeletablePath, false);
        }
    }

    /// <summary>Sets or clears the BSD/Linux immutable flag via the platform's own CLI — .NET has
    /// no portable API for `chflags`/`chattr`. `chattr` needs `CAP_LINUX_IMMUTABLE`, which root has
    /// by default; running this test suite as root is already a known vacuous-pass case elsewhere
    /// (see the class doc on the sibling health tests referenced in the brief).</summary>
    private static void SetImmutable(string path, bool immutable)
    {
        var (fileName, arguments) = OperatingSystem.IsMacOS()
            ? ("chflags", $"{(immutable ? "uchg" : "nouchg")} {path}")
            : ("chattr", $"{(immutable ? "+i" : "-i")} {path}");

        var psi = new System.Diagnostics.ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = System.Diagnostics.Process.Start(psi)!;
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{fileName} {arguments} exited {process.ExitCode}: {process.StandardError.ReadToEnd()}");
        }
    }
}
