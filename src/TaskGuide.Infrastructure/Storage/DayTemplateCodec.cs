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
public static class DayTemplateCodec
{
    public static IReadOnlyList<DayTemplate> Read(string json)
    {
        string? recordIdentity = null;
        return StoreCodecBoundary.Read(
            "day-templates.json",
            "each Day template must satisfy the day-templates.json schema",
            () => ReadCore(json, id => recordIdentity = id),
            () => recordIdentity);
    }

    private static IReadOnlyList<DayTemplate> ReadCore(string json, Action<string?> identify)
    {
        using var document = JsonDocument.Parse(json);

        var templates = new List<DayTemplate>();

        foreach (var element in document.RootElement.EnumerateArray())
        {
            identify(null);
            var id = new DayTemplateId(element.GetProperty("id").GetString()!);
            identify(id.Value);

            var windows = element.GetProperty("windows").EnumerateArray()
                .Select(CodecPrimitives.ReadWindow)
                .ToList();

            var eventPrototypes = element.GetProperty("eventPrototypes").EnumerateArray()
                .Select(ReadEventPrototype)
                .ToList();

            templates.Add(new DayTemplate(id, element.GetProperty("name").GetString()!, windows, eventPrototypes));
        }

        return templates;
    }

    public static void Write(Utf8JsonWriter writer, IReadOnlyList<DayTemplate> templates)
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
