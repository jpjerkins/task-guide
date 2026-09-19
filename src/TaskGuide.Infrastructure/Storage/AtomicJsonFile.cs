using System.Text.Json;

namespace TaskGuide.Infrastructure.Storage;

internal static class AtomicJsonFile
{
    /// <summary>
    /// Write to a temp file in the same directory, fsync it, then rename over the destination.
    /// The rename is atomic on the same filesystem — <paramref name="path"/> is never observable
    /// as a torn or partial write, because nothing touches <paramref name="path"/> itself until
    /// the very last step.
    /// </summary>
    /// <remarks>
    /// Portability caveat: this also fsyncs the file, but not the containing directory — .NET has
    /// no portable API for that (it needs a raw file descriptor and an <c>fsync</c> syscall on the
    /// directory, which is POSIX-specific and unavailable through <see cref="System.IO"/>). On a
    /// host that crashes between the rename and the directory entry reaching stable storage, the
    /// rename could theoretically be lost. Accepted for the walking skeleton; worth revisiting if
    /// pi5's storage stack turns out to need it.
    /// </remarks>
    internal static async Task WriteAsync(string path, Action<Utf8JsonWriter> writeContent, CancellationToken cancellationToken)
    {
        // Every caller supplies a full data-directory file path; the temp must be in that directory.
        var directory = Path.GetDirectoryName(path)!;
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.tmp-{Guid.NewGuid():N}");

        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    writeContent(writer);
                    await writer.FlushAsync(cancellationToken);
                }

                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
