using System.Text.Json;

namespace TaskGuide.Storage.Tests;

/// <summary>
/// Shared assertions applied to every codec in the storage layer. Status is derived and never
/// persisted (#47) — the check here is structural over the emitted JSON, not merely "null",
/// because a codec writing `"status": "active"` as a plain string would slip past every other
/// test in this project.
/// </summary>
public static class CodecAssertions
{
    /// <summary>
    /// Asserts the given JSON object has no `status` property at all — not that it is null,
    /// that it is absent.
    /// </summary>
    public static void NoStatusProperty(JsonElement obj)
    {
        Xunit.Assert.False(obj.TryGetProperty("status", out _), "Expected no `status` property, but one was present.");
    }
}
