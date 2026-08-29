using System.Text.Json;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;

namespace TaskGuide.Infrastructure.Storage;

/// <summary>
/// Reads and writes <c>overrides.json</c> byte-meaningfully against the golden store fixture
/// (`tests/TaskGuide.Storage.Tests/fixtures/data/README.md`): a single date's Windows are always
/// a copy, never a reference (`CONTEXT.md`, "Override") — the copy preserves each Window's id,
/// and the optional <see cref="DayTemplateUse"/> use record carries the template name exactly as
/// it was captured, not resolved by looking the id up in `day-templates.json`.
/// </summary>
public static class OverrideCodec
{
    public static IReadOnlyList<DateOverride> Read(string json)
    {
        using var document = JsonDocument.Parse(json);

        var overrides = new List<DateOverride>();

        foreach (var element in document.RootElement.EnumerateArray())
        {
            var date = CodecPrimitives.ReadDate(element.GetProperty("date"));

            var windows = element.GetProperty("windows").EnumerateArray()
                .Select(CodecPrimitives.ReadWindow)
                .ToList();

            overrides.Add(new DateOverride(date, windows, ReadUsedOrNull(element.GetProperty("used"))));
        }

        return overrides;
    }

    private static DayTemplateUse? ReadUsedOrNull(JsonElement element) =>
        element.ValueKind == JsonValueKind.Null
            ? null
            : new DayTemplateUse(
                new DayTemplateId(element.GetProperty("templateId").GetString()!),
                element.GetProperty("templateName").GetString()!);

    public static void Write(Utf8JsonWriter writer, IReadOnlyList<DateOverride> overrides)
    {
        writer.WriteStartArray();

        foreach (var dateOverride in overrides)
        {
            writer.WriteStartObject();

            CodecPrimitives.WriteDateOrNull(writer, "date", dateOverride.Date);
            WriteUsed(writer, dateOverride.Used);

            writer.WritePropertyName("windows");
            writer.WriteStartArray();
            foreach (var window in dateOverride.Windows) CodecPrimitives.WriteWindow(writer, window);
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteUsed(Utf8JsonWriter writer, DayTemplateUse? used)
    {
        if (used is null)
        {
            writer.WriteNull("used");
            return;
        }

        writer.WritePropertyName("used");
        writer.WriteStartObject();
        writer.WriteString("templateId", used.TemplateId.Value);
        writer.WriteString("templateName", used.TemplateName);
        writer.WriteEndObject();
    }
}
