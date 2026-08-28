using System.Text.Json;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Tasks;

namespace TaskGuide.Infrastructure.Storage;

public static class CompletionCodec
{
    /// <summary>`completions/&lt;taskId&gt;.json` - the id comes from the filename, not the file.</summary>
    public static CompletionLog Read(TaskId taskId, string json)
    {
        using var document = JsonDocument.Parse(json);

        var entries = document.RootElement.EnumerateArray()
            .Select(ReadEntry)
            .ToList();

        return new CompletionLog(taskId, entries);
    }

    public static void Write(Utf8JsonWriter writer, CompletionLog log)
    {
        writer.WriteStartArray();
        foreach (var entry in log.Entries) WriteEntry(writer, entry);
        writer.WriteEndArray();
    }

    /// <summary>`completions/derived.json`.</summary>
    public static IReadOnlyList<DerivedCompletionEntry> ReadDerived(string json)
    {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.EnumerateArray()
            .Select(ReadDerivedEntry)
            .ToList();
    }

    public static void WriteDerived(Utf8JsonWriter writer, IReadOnlyList<DerivedCompletionEntry> entries)
    {
        writer.WriteStartArray();
        foreach (var entry in entries) WriteDerivedEntry(writer, entry);
        writer.WriteEndArray();
    }

    /// <summary>`t_01ARZ....json`. The filename IS the key; nothing inside the file repeats it.</summary>
    public static string FileNameFor(TaskId taskId) => $"{taskId.Value}.json";

    private static CompletionEntry ReadEntry(JsonElement element) =>
        new(
            CodecPrimitives.ReadDateOrNull(element, "due"),
            CodecPrimitives.ReadInstant(element.GetProperty("done")));

    private static void WriteEntry(Utf8JsonWriter writer, CompletionEntry entry)
    {
        writer.WriteStartObject();
        CodecPrimitives.WriteDateOrNull(writer, "due", entry.Due);
        CodecPrimitives.WriteInstant(writer, "done", entry.Done);
        writer.WriteEndObject();
    }

    private static DerivedCompletionEntry ReadDerivedEntry(JsonElement element) =>
        new(
            new RuleId(element.GetProperty("ruleId").GetString()!),
            element.GetProperty("triggerId").GetString()!,
            CodecPrimitives.ReadDate(element.GetProperty("due")),
            CodecPrimitives.ReadInstant(element.GetProperty("done")));

    private static void WriteDerivedEntry(Utf8JsonWriter writer, DerivedCompletionEntry entry)
    {
        writer.WriteStartObject();
        writer.WriteString("ruleId", entry.RuleId.Value);
        writer.WriteString("triggerId", entry.TriggerId);
        CodecPrimitives.WriteDateOrNull(writer, "due", entry.Due);
        CodecPrimitives.WriteInstant(writer, "done", entry.Done);
        writer.WriteEndObject();
    }
}
