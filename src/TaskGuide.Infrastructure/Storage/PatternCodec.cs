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
    public static PatternBook Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var activePatternId = new PatternId(root.GetProperty("activePatternId").GetString()!);

        var patterns = root.GetProperty("patterns").EnumerateArray()
            .Select(ReadPattern)
            .ToList();

        return new PatternBook(activePatternId, patterns);
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

    public static void Write(Utf8JsonWriter writer, PatternBook book)
    {
        writer.WriteStartObject();
        writer.WriteString("activePatternId", book.ActivePatternId.Value);

        writer.WritePropertyName("patterns");
        writer.WriteStartArray();
        foreach (var pattern in book.Patterns) WritePattern(writer, pattern);
        writer.WriteEndArray();

        writer.WriteEndObject();
    }

    private static void WritePattern(Utf8JsonWriter writer, Pattern pattern)
    {
        writer.WriteStartObject();
        writer.WriteString("id", pattern.Id.Value);
        writer.WriteString("name", pattern.Name);

        writer.WritePropertyName("days");
        writer.WriteStartArray();
        foreach (var day in pattern.Days) writer.WriteStringValue(day.Value);
        writer.WriteEndArray();

        writer.WriteEndObject();
    }
}
