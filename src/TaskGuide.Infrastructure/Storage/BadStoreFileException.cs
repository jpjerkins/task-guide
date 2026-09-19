using System.Text.Json;

namespace TaskGuide.Infrastructure.Storage;

/// <summary>
/// A parsed store file violated its codec contract. The physical file, known record identity and
/// violated invariant are separate fields so callers never have to scrape <see cref="Exception.Message"/>.
/// </summary>
public sealed class BadStoreFileException(
    string filePath,
    string? recordIdentity,
    string invariant,
    Exception innerException)
    : Exception(BuildMessage(filePath, recordIdentity, invariant, innerException), innerException)
{
    public string FilePath { get; } = filePath;
    public string? RecordIdentity { get; } = recordIdentity;
    public string Invariant { get; } = invariant;

    private static string BuildMessage(string filePath, string? recordIdentity, string invariant, Exception innerException) =>
        recordIdentity is null
            ? $"Store file '{filePath}' violates '{invariant}': {innerException.Message}"
            : $"Store file '{filePath}' record '{recordIdentity}' violates '{invariant}': {innerException.Message}";
}

internal static class StoreCodecBoundary
{
    public static T Read<T>(string filePath, string invariant, Func<T> read, Func<string?>? recordIdentity = null)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new BadStoreFileException(filePath, recordIdentity?.Invoke(), invariant, exception);
        }
    }
}
