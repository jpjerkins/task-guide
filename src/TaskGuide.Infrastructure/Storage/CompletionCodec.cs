using System.Text.Json;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Tasks;

namespace TaskGuide.Infrastructure.Storage;

/// <summary>
/// A derived completion's identity: (ruleId, triggerId, due) — exactly the key
/// <c>Completion.cs</c> documents `completions/derived.json` as being keyed on.
/// </summary>
public readonly record struct DerivedCompletionKey(RuleId RuleId, string TriggerId, DateOnly Due);

public static class CompletionCodec
{
    /// <summary>
    /// `completions/&lt;taskId&gt;.json` - the id comes from the filename, not the file.
    /// </summary>
    public static CompletionLog Read(TaskId taskId, string json)
    {
        using var document = JsonDocument.Parse(json);

        var entries = new List<CompletionEntry>();

        foreach (var element in document.RootElement.EnumerateArray())
        {
            entries.Add(ReadEntry(element));
        }

        return new CompletionLog(taskId, entries);
    }

    public static void Write(Utf8JsonWriter writer, CompletionLog log)
    {
        writer.WriteStartArray();

        foreach (var entry in log.Entries)
        {
            writer.WriteStartObject();
            WriteEntryBody(writer, entry);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    /// <summary>`completions/derived.json`.</summary>
    public static IReadOnlyList<DerivedCompletionEntry> ReadDerived(string json)
    {
        using var document = JsonDocument.Parse(json);

        var entries = new List<DerivedCompletionEntry>();

        foreach (var element in document.RootElement.EnumerateArray())
        {
            entries.Add(ReadDerivedEntry(element));
        }

        RejectDuplicateKeys(entries);

        return entries;
    }

    private static void RejectDuplicateKeys(IReadOnlyList<DerivedCompletionEntry> entries)
    {
        var duplicate = entries
            .GroupBy(KeyOf)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is null) return;

        throw new JsonException(
            $"Derived completion has duplicate key (ruleId, triggerId, due)=({duplicate.Key.RuleId.Value}, {duplicate.Key.TriggerId}, {duplicate.Key.Due:yyyy-MM-dd}).");
    }

    public static void WriteDerived(Utf8JsonWriter writer, IReadOnlyList<DerivedCompletionEntry> entries)
    {
        writer.WriteStartArray();

        foreach (var entry in entries)
        {
            writer.WriteStartObject();
            WriteDerivedEntryBody(writer, entry);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static DerivedCompletionKey KeyOf(DerivedCompletionEntry entry) =>
        new(entry.RuleId, entry.TriggerId, entry.Due);

    /// <summary>`t_01ARZ....json`. The filename IS the key; nothing inside the file repeats it.</summary>
    public static string FileNameFor(TaskId taskId) => $"{taskId.Value}.json";

    private static CompletionEntry ReadEntry(JsonElement element) =>
        new(
            CodecPrimitives.ReadDateOrNull(element, "due"),
            CodecPrimitives.ReadInstant(element.GetProperty("done")));

    private static void WriteEntryBody(Utf8JsonWriter writer, CompletionEntry entry)
    {
        CodecPrimitives.WriteDateOrNull(writer, "due", entry.Due);
        CodecPrimitives.WriteInstant(writer, "done", entry.Done);
    }

    private static DerivedCompletionEntry ReadDerivedEntry(JsonElement element) =>
        new(
            new RuleId(element.GetProperty("ruleId").GetString()!),
            element.GetProperty("triggerId").GetString()!,
            CodecPrimitives.ReadDate(element.GetProperty("due")),
            CodecPrimitives.ReadInstant(element.GetProperty("done")));

    private static void WriteDerivedEntryBody(Utf8JsonWriter writer, DerivedCompletionEntry entry)
    {
        writer.WriteString("ruleId", entry.RuleId.Value);
        writer.WriteString("triggerId", entry.TriggerId);
        CodecPrimitives.WriteDateOrNull(writer, "due", entry.Due);
        CodecPrimitives.WriteInstant(writer, "done", entry.Done);
    }
}
