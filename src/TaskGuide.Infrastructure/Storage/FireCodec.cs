using System.Text.Json;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Firing;

namespace TaskGuide.Infrastructure.Storage;

/// <summary>
/// A Fire row's identity within its day: (windowId, kind). The `fires/&lt;date&gt;.json` row carries
/// no id of its own, and this pair is already the uniqueness key `FireCodec` enforces at read.
/// </summary>
public readonly record struct FireKey(WindowId? WindowId, FireKind Kind);

public static class FireCodec
{
    private const string FileDateFormat = "yyyy-MM-dd";

    private static readonly string[] KnownFireFields =
        ["windowId", "kind", "windowName", "windowStart", "windowEnd", "dueAt", "firedAt", "matched", "carried"];

    /// <summary>`fires/&lt;date&gt;.json` - the date comes from the filename.</summary>
    public static (DayFires Fires, IReadOnlyDictionary<FireKey, IReadOnlyList<KeyValuePair<string, JsonElement>>> Extras)
        Read(DateOnly date, string json)
    {
        using var document = JsonDocument.Parse(json);

        var rows = new List<FireRow>();
        var extras = new Dictionary<FireKey, IReadOnlyList<KeyValuePair<string, JsonElement>>>();

        foreach (var element in document.RootElement.EnumerateArray())
        {
            var row = ReadRow(element);
            rows.Add(row);

            var extra = CodecPrimitives.UnknownFields(element, KnownFireFields);
            if (extra.Count > 0) extras[KeyOf(row)] = extra;
        }

        RejectDuplicateKeys(date, rows);

        return (new DayFires(date, rows), extras);
    }

    public static void Write(
        Utf8JsonWriter writer,
        DayFires fires,
        IReadOnlyDictionary<FireKey, IReadOnlyList<KeyValuePair<string, JsonElement>>> extras)
    {
        writer.WriteStartArray();
        foreach (var row in fires.Rows)
        {
            writer.WriteStartObject();
            WriteRowBody(writer, row);
            if (extras.TryGetValue(KeyOf(row), out var extra)) CodecPrimitives.WriteUnknownFields(writer, extra);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    /// <summary>
    /// The key a Fire row's unknown fields are stored against: exactly the key
    /// <see cref="RejectDuplicateKeys"/> enforces uniqueness on, so the two cannot drift apart.
    /// </summary>
    private static FireKey KeyOf(FireRow row) => new(row.WindowId, row.Kind);

    public static string FileNameFor(DateOnly date) => $"{date:yyyy-MM-dd}.json";

    public static DateOnly? DateFromFileName(string fileName)
    {
        if (!fileName.EndsWith(".json", StringComparison.Ordinal)) return null;

        var withoutExtension = fileName[..^".json".Length];
        return DateOnly.TryParseExact(withoutExtension, FileDateFormat, out var date) ? date : null;
    }

    private static FireRow ReadRow(JsonElement element) =>
        new(
            ReadWindowIdOrNull(element, "windowId"),
            ReadKind(element.GetProperty("kind")),
            ReadStringOrNull(element, "windowName"),
            CodecPrimitives.ReadClockTimeOrNull(element, "windowStart"),
            CodecPrimitives.ReadClockTimeOrNull(element, "windowEnd"),
            CodecPrimitives.ReadInstantOrNull(element, "dueAt"),
            CodecPrimitives.ReadInstantOrNull(element, "firedAt"),
            ReadIntOrNull(element, "matched"),
            ReadEventIdOrNull(element, "carried"));

    private static void WriteRowBody(Utf8JsonWriter writer, FireRow row)
    {
        WriteWindowIdOrNull(writer, "windowId", row.WindowId);
        writer.WriteString("kind", WriteKind(row.Kind));
        WriteStringOrNull(writer, "windowName", row.WindowName);
        CodecPrimitives.WriteClockTimeOrNull(writer, "windowStart", row.WindowStart);
        CodecPrimitives.WriteClockTimeOrNull(writer, "windowEnd", row.WindowEnd);
        CodecPrimitives.WriteInstantOrNull(writer, "dueAt", row.DueAt);
        CodecPrimitives.WriteInstantOrNull(writer, "firedAt", row.FiredAt);
        WriteIntOrNull(writer, "matched", row.Matched);
        WriteEventIdOrNull(writer, "carried", row.Carried);
    }

    private static void RejectDuplicateKeys(DateOnly date, IReadOnlyList<FireRow> rows)
    {
        var duplicate = rows
            .GroupBy(KeyOf)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is null) return;

        var window = duplicate.Key.WindowId?.Value ?? "null";
        throw new JsonException(
            $"Fire record {date:yyyy-MM-dd} has duplicate key (date, windowId, kind)=({date:yyyy-MM-dd}, {window}, {WriteKind(duplicate.Key.Kind)}).");
    }

    private static WindowId? ReadWindowIdOrNull(JsonElement element, string property)
    {
        var value = element.GetProperty(property);
        return value.ValueKind == JsonValueKind.Null ? null : new WindowId(value.GetString()!);
    }

    private static EventId? ReadEventIdOrNull(JsonElement element, string property)
    {
        var value = element.GetProperty(property);
        return value.ValueKind == JsonValueKind.Null ? null : new EventId(value.GetString()!);
    }

    private static string? ReadStringOrNull(JsonElement element, string property)
    {
        var value = element.GetProperty(property);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetString();
    }

    private static int? ReadIntOrNull(JsonElement element, string property)
    {
        var value = element.GetProperty(property);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetInt32();
    }

    private static FireKind ReadKind(JsonElement element) => element.GetString() switch
    {
        "window" => FireKind.Window,
        "unconditional" => FireKind.Unconditional,
        "snooze" => FireKind.Snooze,
        "fallback" => FireKind.Fallback,
        var kind => throw new JsonException($"Unknown FireKind '{kind}'"),
    };

    private static string WriteKind(FireKind kind) => kind switch
    {
        FireKind.Window => "window",
        FireKind.Unconditional => "unconditional",
        FireKind.Snooze => "snooze",
        FireKind.Fallback => "fallback",
        _ => throw new JsonException($"Unknown FireKind '{kind}'"),
    };

    private static void WriteWindowIdOrNull(Utf8JsonWriter writer, string property, WindowId? value)
    {
        if (value is { } id) writer.WriteString(property, id.Value);
        else writer.WriteNull(property);
    }

    private static void WriteEventIdOrNull(Utf8JsonWriter writer, string property, EventId? value)
    {
        if (value is { } id) writer.WriteString(property, id.Value);
        else writer.WriteNull(property);
    }

    private static void WriteStringOrNull(Utf8JsonWriter writer, string property, string? value)
    {
        if (value is null) writer.WriteNull(property);
        else writer.WriteString(property, value);
    }

    private static void WriteIntOrNull(Utf8JsonWriter writer, string property, int? value)
    {
        if (value is { } number) writer.WriteNumber(property, number);
        else writer.WriteNull(property);
    }
}
