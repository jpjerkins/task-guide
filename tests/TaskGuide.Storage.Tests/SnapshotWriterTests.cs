using System.Text.Json;
using TaskGuide.Infrastructure.Storage;
using Xunit;

namespace TaskGuide.Storage.Tests;

/// <summary>
/// Against `tests/TEST-INVENTORY.md`'s "`manifest.json` round-trips its version" — the golden
/// fixture at `fixtures/data/manifest.json` is `{ "version": 1 }`.
/// </summary>
public sealed class ManifestCodecTests
{
    [Fact]
    public void Manifest_json_round_trips_its_version()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            ManifestCodec.Write(writer, 1);
        }

        var written = System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        Assert.Equal(1, ManifestCodec.Read(written));

        var fixturePath = Path.Combine(FindRepoRoot(), "tests", "TaskGuide.Storage.Tests", "fixtures", "data", "manifest.json");
        Assert.Equal(1, ManifestCodec.Read(File.ReadAllText(fixturePath)));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "task-guide.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not find repo root (task-guide.slnx) above " + AppContext.BaseDirectory);
    }
}

/// <summary>
/// Against `tests/TEST-INVENTORY.md`'s "Sequential · TaskGuide.Storage.Tests" section:
/// "snapshots keep the last 5", "a Snapshot is a whole-file copy, not a re-serialisation", and
/// "a Snapshot recreates the relative directory structure of the paths it is given".
/// </summary>
public sealed class SnapshotWriterTests : IDisposable
{
    private readonly string _dataDir = Directory.CreateTempSubdirectory("taskguide-storage-tests-").FullName;

    public void Dispose() => Directory.Delete(_dataDir, recursive: true);

    [Fact]
    public async Task Snapshots_keep_the_last_5()
    {
        File.WriteAllText(Path.Combine(_dataDir, "manifest.json"), "{ \"version\": 1 }");
        var writer = new SnapshotWriter(_dataDir);
        var snapshotsDir = Path.Combine(_dataDir, "snapshots");

        var start = new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 6; i++)
        {
            await writer.TakeAsync(["manifest.json"], start.AddSeconds(i), CancellationToken.None);
        }

        var remaining = Directory.GetDirectories(snapshotsDir);
        Assert.Equal(5, remaining.Length);
    }

    [Fact]
    public async Task A_snapshot_is_a_whole_file_copy_not_a_re_serialisation()
    {
        var badJson = "{ this is not valid json at all";
        File.WriteAllText(Path.Combine(_dataDir, "tasks.json"), badJson);
        var writer = new SnapshotWriter(_dataDir);

        var snapshotDir = await writer.TakeAsync(["tasks.json"], DateTimeOffset.UtcNow, CancellationToken.None);

        var copied = File.ReadAllText(Path.Combine(snapshotDir, "tasks.json"));
        Assert.Equal(badJson, copied);
    }

    [Fact]
    public async Task A_snapshot_recreates_the_relative_directory_structure_of_the_paths_it_is_given()
    {
        var completionsDir = Path.Combine(_dataDir, "completions");
        Directory.CreateDirectory(completionsDir);
        File.WriteAllText(Path.Combine(completionsDir, "t_01ARZ3NDEKTSV4RRFFQ69G5FAV.json"), "[]");
        var writer = new SnapshotWriter(_dataDir);
        var now = new DateTimeOffset(2026, 8, 28, 22, 45, 3, TimeSpan.Zero);

        var snapshotDir = await writer.TakeAsync(
            ["completions/t_01ARZ3NDEKTSV4RRFFQ69G5FAV.json"], now, CancellationToken.None);

        Assert.Equal(Path.Combine(_dataDir, "snapshots"), Path.GetDirectoryName(snapshotDir));
        Assert.True(File.Exists(Path.Combine(snapshotDir, "completions", "t_01ARZ3NDEKTSV4RRFFQ69G5FAV.json")));
    }
}
