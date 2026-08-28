namespace TaskGuide.Infrastructure.Storage;

/// <summary>
/// What <see cref="FireRetention.Sweep"/> did: every date whose file was actually unlinked, and
/// every date whose file the sweep tried and failed to unlink. A date appearing in
/// <see cref="Failed"/> still has its file on disk — the sweep does not remove, rename, or
/// otherwise touch a file it could not delete.
/// </summary>
public sealed record FireSweepResult(IReadOnlyList<DateOnly> Removed, IReadOnlyList<DateOnly> Failed);

public static class FireRetention
{
    /// <summary>
    /// Deletes every `fires/&lt;date&gt;.json` older than 30 days. A per-file delete failure
    /// (a lock, a permissions error) is caught and recorded rather than left to propagate: per
    /// <c>IHealth.cs</c>, Liveness reads write health off this sweep's outcome rather than probing
    /// separately, so letting one bad file abort the whole sweep would mean <em>no</em> write-health
    /// signal at all — silent exactly when something is wrong — instead of a degraded one. Only
    /// <see cref="IOException"/> and <see cref="UnauthorizedAccessException"/> are caught: both are
    /// the filesystem telling the caller a delete did not happen, as distinct from a programming
    /// error, which should still crash the sweep. A file whose name does not parse as a date is
    /// neither removed nor failed — it is not a fire file the retention window applies to at all.
    /// </summary>
    public static FireSweepResult Sweep(string dataDir, DateOnly today)
    {
        var firesDir = Path.Combine(dataDir, "fires");
        if (!Directory.Exists(firesDir)) return new FireSweepResult([], []);

        var removed = new List<DateOnly>();
        var failed = new List<DateOnly>();

        foreach (var path in Directory.EnumerateFiles(firesDir))
        {
            var date = FireCodec.DateFromFileName(Path.GetFileName(path));
            if (date is null || today.DayNumber - date.Value.DayNumber <= 30) continue;

            try
            {
                File.Delete(path);
                removed.Add(date.Value);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add(date.Value);
            }
        }

        return new FireSweepResult(removed, failed);
    }
}
