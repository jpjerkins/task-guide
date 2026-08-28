using System.Text.Json;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;

namespace TaskGuide.Infrastructure.Storage;

/// <summary>
/// Reads and writes <c>day-templates.json</c> byte-meaningfully against the golden store fixture
/// (`tests/TaskGuide.Storage.Tests/fixtures/data/README.md`): a Day template holds Availability
/// Windows and, optionally, Event prototypes — both dateless, becoming per-day instances only on
/// application (`CONTEXT.md`, "Day template").
/// </summary>
/// <remarks>
/// A property this binary does not know about is preserved verbatim on every field it did not
/// touch, keyed per Day template — the same convention `TaskCodec` uses per Task.
/// </remarks>
public static class DayTemplateCodec
{
    private static readonly string[] KnownFields = ["id", "name", "windows", "eventPrototypes"];

    public static (IReadOnlyList<DayTemplate> Templates,
        IReadOnlyDictionary<DayTemplateId, IReadOnlyList<KeyValuePair<string, JsonElement>>> Extras)
        Read(string json)
    {
        using var document = JsonDocument.Parse(json);

        var templates = new List<DayTemplate>();
        var extras = new Dictionary<DayTemplateId, IReadOnlyList<KeyValuePair<string, JsonElement>>>();

        foreach (var element in document.RootElement.EnumerateArray())
        {
            var id = new DayTemplateId(element.GetProperty("id").GetString()!);

            var windows = element.GetProperty("windows").EnumerateArray()
                .Select(CodecPrimitives.ReadWindow)
                .ToList();

            var eventPrototypes = element.GetProperty("eventPrototypes").EnumerateArray()
                .Select(ReadEventPrototype)
                .ToList();

            templates.Add(new DayTemplate(id, element.GetProperty("name").GetString()!, windows, eventPrototypes));

            var extra = CodecPrimitives.UnknownFields(element, KnownFields);
            if (extra.Count > 0) extras[id] = extra;
        }

        return (templates, extras);
    }

    public static void Write(
        Utf8JsonWriter writer,
        IReadOnlyList<DayTemplate> templates,
        IReadOnlyDictionary<DayTemplateId, IReadOnlyList<KeyValuePair<string, JsonElement>>> extras)
    {
        writer.WriteStartArray();

        foreach (var template in templates)
        {
            writer.WriteStartObject();

            writer.WriteString("id", template.Id.Value);
            writer.WriteString("name", template.Name);

            writer.WritePropertyName("windows");
            writer.WriteStartArray();
            foreach (var window in template.Windows) CodecPrimitives.WriteWindow(writer, window);
            writer.WriteEndArray();

            writer.WritePropertyName("eventPrototypes");
            writer.WriteStartArray();
            foreach (var eventPrototype in template.EventPrototypes) WriteEventPrototype(writer, eventPrototype);
            writer.WriteEndArray();

            if (extras.TryGetValue(template.Id, out var extra)) CodecPrimitives.WriteUnknownFields(writer, extra);

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static EventPrototype ReadEventPrototype(JsonElement element) =>
        new(
            new EventPrototypeId(element.GetProperty("id").GetString()!),
            element.GetProperty("name").GetString()!,
            CodecPrimitives.ReadClockTime(element.GetProperty("start")),
            CodecPrimitives.ReadClockTime(element.GetProperty("end")),
            CodecPrimitives.ReadTagSet(element),
            CodecPrimitives.ReadOffsetOrNull(element, "absenceNotice"));

    private static void WriteEventPrototype(Utf8JsonWriter writer, EventPrototype eventPrototype)
    {
        writer.WriteStartObject();
        writer.WriteString("id", eventPrototype.Id.Value);
        writer.WriteString("name", eventPrototype.Name);
        CodecPrimitives.WriteClockTime(writer, "start", eventPrototype.Start);
        CodecPrimitives.WriteClockTime(writer, "end", eventPrototype.End);
        CodecPrimitives.WriteTagSet(writer, eventPrototype.Tags);
        CodecPrimitives.WriteOffsetOrNull(writer, "absenceNotice", eventPrototype.AbsenceNotice);
        writer.WriteEndObject();
    }
}
