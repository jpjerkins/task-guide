using System.Text.Json;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;

namespace TaskGuide.Infrastructure.Storage;

/// <summary>
/// Reads and writes <c>patterns.json</c> byte-meaningfully against the golden store fixture
/// (`tests/TaskGuide.Storage.Tests/fixtures/data/README.md`): an <b>object</b>, not an array —
/// `{ activePatternId, patterns: [...] }` — because "which Pattern is active" is the only
/// singleton fact the store carries (`CONTEXT.md`, "Pattern").
/// </summary>
/// <remarks>
/// A Pattern's <c>days</c> is exactly seven <see cref="DayTemplateId"/>s indexed by
/// <see cref="DayOfWeek"/> (Sunday = 0), matching <see cref="Pattern.this[DayOfWeek]"/>. A
/// <c>days</c> array of any other length is rejected at read, named by Pattern, rather than left
/// to throw an index error far from the cause the first time a weekday off the end is looked up.
/// </remarks>
public static class PatternCodec
{
    private static readonly string[] KnownEnvelopeFields = ["activePatternId", "patterns"];
    private static readonly string[] KnownPatternFields = ["id", "name", "days"];

    /// <summary>
    /// Unknown fields arrive at two levels here, because `patterns.json` is an object: one channel
    /// keyed per <see cref="PatternId"/>, and one for the envelope itself. Both must survive, or a
    /// rollback loses whichever level a newer version wrote to.
    /// </summary>
    public static (PatternBook Book,
        IReadOnlyDictionary<PatternId, IReadOnlyList<KeyValuePair<string, JsonElement>>> Extras,
        IReadOnlyList<KeyValuePair<string, JsonElement>> EnvelopeExtras)
        Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var activePatternId = new PatternId(root.GetProperty("activePatternId").GetString()!);

        var patterns = new List<Pattern>();
        var extras = new Dictionary<PatternId, IReadOnlyList<KeyValuePair<string, JsonElement>>>();

        foreach (var element in root.GetProperty("patterns").EnumerateArray())
        {
            var pattern = ReadPattern(element);
            patterns.Add(pattern);

            var extra = CodecPrimitives.UnknownFields(element, KnownPatternFields);
            if (extra.Count > 0) extras[pattern.Id] = extra;
        }

        var envelopeExtras = CodecPrimitives.UnknownFields(root, KnownEnvelopeFields);

        return (new PatternBook(activePatternId, patterns), extras, envelopeExtras);
    }

    private static Pattern ReadPattern(JsonElement element)
    {
        var name = element.GetProperty("name").GetString()!;

        var days = element.GetProperty("days").EnumerateArray()
            .Select(d => new DayTemplateId(d.GetString()!))
            .ToList();

        if (days.Count != 7)
        {
            throw new JsonException(
                $"Pattern '{name}' has a `days` array of length {days.Count}; a Pattern must name exactly seven days.");
        }

        return new Pattern(new PatternId(element.GetProperty("id").GetString()!), name, days);
    }

    public static void Write(
        Utf8JsonWriter writer,
        PatternBook book,
        IReadOnlyDictionary<PatternId, IReadOnlyList<KeyValuePair<string, JsonElement>>> extras,
        IReadOnlyList<KeyValuePair<string, JsonElement>> envelopeExtras)
    {
        writer.WriteStartObject();
        writer.WriteString("activePatternId", book.ActivePatternId.Value);

        writer.WritePropertyName("patterns");
        writer.WriteStartArray();
        foreach (var pattern in book.Patterns) WritePattern(writer, pattern, extras);
        writer.WriteEndArray();

        CodecPrimitives.WriteUnknownFields(writer, envelopeExtras);

        writer.WriteEndObject();
    }

    private static void WritePattern(
        Utf8JsonWriter writer,
        Pattern pattern,
        IReadOnlyDictionary<PatternId, IReadOnlyList<KeyValuePair<string, JsonElement>>> extras)
    {
        writer.WriteStartObject();
        writer.WriteString("id", pattern.Id.Value);
        writer.WriteString("name", pattern.Name);

        writer.WritePropertyName("days");
        writer.WriteStartArray();
        foreach (var day in pattern.Days) writer.WriteStringValue(day.Value);
        writer.WriteEndArray();

        if (extras.TryGetValue(pattern.Id, out var extra)) CodecPrimitives.WriteUnknownFields(writer, extra);

        writer.WriteEndObject();
    }
}
