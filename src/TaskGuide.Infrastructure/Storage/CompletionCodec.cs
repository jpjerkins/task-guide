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
    private static readonly string[] KnownEntryFields = ["due", "done"];
    private static readonly string[] KnownDerivedFields = ["ruleId", "triggerId", "due", "done"];

    /// <summary>
    /// `completions/&lt;taskId&gt;.json` - the id comes from the filename, not the file.
    /// </summary>
    /// <remarks>
    /// Unknown fields are keyed by <b>entry index</b>. A <see cref="CompletionEntry"/> has no id,
    /// and `due` is null for a one-off Task's entry, so no field of the entry can serve as a key.
    /// The index is stable across a load-and-save round trip because <see cref="Write"/> emits
    /// entries in read order — which is the property rollback losslessness actually needs.
    /// </remarks>
    public static (CompletionLog Log, IReadOnlyDictionary<int, IReadOnlyList<KeyValuePair<string, JsonElement>>> Extras)
        Read(TaskId taskId, string json)
    {
        using var document = JsonDocument.Parse(json);

        var entries = new List<CompletionEntry>();
        var extras = new Dictionary<int, IReadOnlyList<KeyValuePair<string, JsonElement>>>();

        foreach (var element in document.RootElement.EnumerateArray())
        {
            var extra = CodecPrimitives.UnknownFields(element, KnownEntryFields);
            if (extra.Count > 0) extras[entries.Count] = extra;

            entries.Add(ReadEntry(element));
        }

        return (new CompletionLog(taskId, entries), extras);
    }

    public static void Write(
        Utf8JsonWriter writer,
        CompletionLog log,
        IReadOnlyDictionary<int, IReadOnlyList<KeyValuePair<string, JsonElement>>> extras)
    {
        writer.WriteStartArray();

        for (var index = 0; index < log.Entries.Count; index++)
        {
            writer.WriteStartObject();
            WriteEntryBody(writer, log.Entries[index]);
            if (extras.TryGetValue(index, out var extra)) CodecPrimitives.WriteUnknownFields(writer, extra);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    /// <summary>`completions/derived.json`.</summary>
    public static (IReadOnlyList<DerivedCompletionEntry> Entries,
        IReadOnlyDictionary<DerivedCompletionKey, IReadOnlyList<KeyValuePair<string, JsonElement>>> Extras)
        ReadDerived(string json)
    {
        using var document = JsonDocument.Parse(json);

        var entries = new List<DerivedCompletionEntry>();
        var extras = new Dictionary<DerivedCompletionKey, IReadOnlyList<KeyValuePair<string, JsonElement>>>();

        foreach (var element in document.RootElement.EnumerateArray())
        {
            var entry = ReadDerivedEntry(element);
            entries.Add(entry);

            var extra = CodecPrimitives.UnknownFields(element, KnownDerivedFields);
            if (extra.Count > 0) extras[KeyOf(entry)] = extra;
        }

        RejectDuplicateKeys(entries);

        return (entries, extras);
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

    public static void WriteDerived(
        Utf8JsonWriter writer,
        IReadOnlyList<DerivedCompletionEntry> entries,
        IReadOnlyDictionary<DerivedCompletionKey, IReadOnlyList<KeyValuePair<string, JsonElement>>> extras)
    {
        writer.WriteStartArray();

        foreach (var entry in entries)
        {
            writer.WriteStartObject();
            WriteDerivedEntryBody(writer, entry);
            if (extras.TryGetValue(KeyOf(entry), out var extra)) CodecPrimitives.WriteUnknownFields(writer, extra);
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
