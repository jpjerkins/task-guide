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
/// <remarks>
/// A property this binary does not know about is preserved verbatim on every field it did not
/// touch, keyed per date — the same convention `TaskCodec` and `DayTemplateCodec` use.
/// </remarks>
public static class OverrideCodec
{
    private static readonly string[] KnownFields = ["date", "used", "windows"];

    public static (IReadOnlyList<DateOverride> Overrides,
        IReadOnlyDictionary<DateOnly, IReadOnlyList<KeyValuePair<string, JsonElement>>> Extras)
        Read(string json)
    {
        using var document = JsonDocument.Parse(json);

        var overrides = new List<DateOverride>();
        var extras = new Dictionary<DateOnly, IReadOnlyList<KeyValuePair<string, JsonElement>>>();

        foreach (var element in document.RootElement.EnumerateArray())
        {
            var date = CodecPrimitives.ReadDate(element.GetProperty("date"));

            var windows = element.GetProperty("windows").EnumerateArray()
                .Select(CodecPrimitives.ReadWindow)
                .ToList();

            overrides.Add(new DateOverride(date, windows, ReadUsedOrNull(element.GetProperty("used"))));

            var extra = CodecPrimitives.UnknownFields(element, KnownFields);
            if (extra.Count > 0) extras[date] = extra;
        }

        return (overrides, extras);
    }

    private static DayTemplateUse? ReadUsedOrNull(JsonElement element) =>
        element.ValueKind == JsonValueKind.Null
            ? null
            : new DayTemplateUse(
                new DayTemplateId(element.GetProperty("templateId").GetString()!),
                element.GetProperty("templateName").GetString()!);

    public static void Write(
        Utf8JsonWriter writer,
        IReadOnlyList<DateOverride> overrides,
        IReadOnlyDictionary<DateOnly, IReadOnlyList<KeyValuePair<string, JsonElement>>> extras)
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

            if (extras.TryGetValue(dateOverride.Date, out var extra)) CodecPrimitives.WriteUnknownFields(writer, extra);

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
