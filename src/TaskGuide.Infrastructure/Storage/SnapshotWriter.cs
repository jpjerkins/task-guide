namespace TaskGuide.Infrastructure.Storage;

/// <summary>
/// `/data/snapshots/&lt;utc&gt;/` — whole-file copies, taken before a startup that will migrate
/// or sweep the registry, keeping the last 5 (<see cref="StartupWriter.ApplyAsync"/>).
/// A Snapshot sits <em>on</em> the protected volume and guards against this service's own writes;
/// see the Backup entry in CONTEXT.md for the mechanism that guards against losing the volume —
/// the two are never one word.
/// <para>Copies bytes, never re-serialises: a file the current binary cannot parse is exactly the
/// case a Snapshot exists to have already copied faithfully.</para>
/// </summary>
public sealed class SnapshotWriter(string dataDir)
{
    /// <summary>
    /// Not one of the three stored date/time encodings (clock time, calendar date, recorded
    /// instant) — this is a filesystem name, chosen only to sort chronologically as a string and
    /// stay legal on a POSIX filesystem, so it avoids the colons a recorded instant would carry.
    /// </summary>
    private const string DirectoryNameFormat = "yyyyMMdd'T'HHmmss'Z'";

    private const int SnapshotsToKeep = 5;

    /// <summary>
    /// Copies every path in <paramref name="relativePaths"/> (relative to <paramref name="dataDir"/>,
    /// e.g. <c>completions/t_01ARZ3NDEKTSV4RRFFQ69G5FAV.json</c>) into a new
    /// <c>snapshots/&lt;utc&gt;/</c> directory, recreating each path's relative subdirectory so the
    /// snapshot mirrors the store's own layout and is restorable with a plain <c>cp</c>. Prunes
    /// older snapshot directories down to the last 5 once the copy completes. Returns the
    /// directory it wrote.
    /// </summary>
    public async Task<string> TakeAsync(IReadOnlyList<string> relativePaths, DateTimeOffset now, CancellationToken ct)
    {
        var snapshotsDir = Path.Combine(dataDir, "snapshots");
        var targetDir = Path.Combine(snapshotsDir, now.UtcDateTime.ToString(DirectoryNameFormat));
        Directory.CreateDirectory(targetDir);

        foreach (var relativePath in relativePaths)
        {
            var source = Path.Combine(dataDir, relativePath);
            var destination = Path.Combine(targetDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            await using var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using var destinationStream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await sourceStream.CopyToAsync(destinationStream, ct);
        }

        Prune(snapshotsDir);

        return targetDir;
    }

    private static void Prune(string snapshotsDir)
    {
        var directories = Directory.GetDirectories(snapshotsDir).OrderBy(d => d, StringComparer.Ordinal).ToArray();
        var toRemove = directories.Length - SnapshotsToKeep;

        for (var i = 0; i < toRemove; i++)
        {
            Directory.Delete(directories[i], recursive: true);
        }
    }
}
