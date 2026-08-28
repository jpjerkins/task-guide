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

        var removed = FireRetention.Sweep(_dataDir, new DateOnly(2026, 8, 28));

        Assert.Equal(1, removed);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(retained));
    }

    [Fact]
    public void A_fire_file_exactly_30_days_old_is_kept_the_boundary_must_not_drift()
    {
        var boundary = SeedFireFile(new DateOnly(2026, 7, 29));

        var removed = FireRetention.Sweep(_dataDir, new DateOnly(2026, 8, 28));

        Assert.Equal(0, removed);
        Assert.True(File.Exists(boundary));
    }

    [Fact]
    public void A_file_in_fires_whose_name_is_not_a_date_is_left_untouched()
    {
        var firesDir = Path.Combine(_dataDir, "fires");
        Directory.CreateDirectory(firesDir);
        var unexpected = Path.Combine(firesDir, "not-a-fire.json");
        File.WriteAllText(unexpected, "this is not json");

        var removed = FireRetention.Sweep(_dataDir, new DateOnly(2026, 8, 28));

        Assert.Equal(0, removed);
        Assert.True(File.Exists(unexpected));
    }

    [Fact]
    public void The_sweep_on_an_absent_fires_directory_is_a_no_op_not_an_error()
    {
        var removed = FireRetention.Sweep(_dataDir, new DateOnly(2026, 8, 28));

        Assert.Equal(0, removed);
        Assert.False(Directory.Exists(Path.Combine(_dataDir, "fires")));
    }
}
