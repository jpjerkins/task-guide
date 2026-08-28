using System.Text.Json;

namespace TaskGuide.Infrastructure.Storage;

/// <summary>
/// `manifest.json` — the store-wide version: <c>{ "version": 1 }</c>, nothing else. Per ADR-0001
/// this is the one file a restore cannot omit; per <see cref="Application.Ports.IStartupSequence"/>
/// it is what the migration steps read and advance.
/// </summary>
public static class ManifestCodec
{
    /// <summary>The version this binary writes. Not necessarily the version it can read.</summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// Throws <see cref="JsonException"/> if <paramref name="json"/> is not a JSON object, or has
    /// no `version` property, or `version` is not an integer. A manifest this can't make sense of
    /// is exactly the case a Snapshot exists to have already copied faithfully, so this refuses
    /// rather than guessing.
    /// </summary>
    public static int Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("version", out var version))
        {
            throw new JsonException("manifest.json is missing its \"version\" property.");
        }

        return version.GetInt32();
    }

    public static void Write(Utf8JsonWriter writer, int version)
    {
        writer.WriteStartObject();
        writer.WriteNumber("version", version);
        writer.WriteEndObject();
    }
}
