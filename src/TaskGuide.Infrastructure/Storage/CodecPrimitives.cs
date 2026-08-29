using System.Globalization;
using System.Text.Json;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;

namespace TaskGuide.Infrastructure.Storage;

/// <summary>
/// The shared JSON codec primitives every store codec is built from: the three date/time
/// encodings (`TaskCodec` fixture README — authored clock times, calendar dates, recorded
/// instants), <see cref="TagSet"/>, <see cref="Offset"/>, and the Availability Window shape
/// shared by `day-templates.json` and `overrides.json`.
/// </summary>
/// <remarks>
/// Extracted from `TaskCodec`, which was the first and, until now, only codec. Behaviour is
/// unchanged — every helper here reproduces exactly what `TaskCodec` wrote before extraction.
/// </remarks>
public static class CodecPrimitives
{
    private const string DateFormat = "yyyy-MM-dd";
    private const string ClockTimeFormat = "HH:mm";
    private const string InstantFormat = "yyyy-MM-ddTHH:mm:ssZ";

    // ---- Calendar dates ----

    public static DateOnly ReadDate(JsonElement e) => DateOnly.ParseExact(e.GetString()!, DateFormat);

    public static DateOnly? ReadDateOrNull(JsonElement parent, string property)
    {
        var value = parent.GetProperty(property);
        return value.ValueKind == JsonValueKind.Null ? null : ReadDate(value);
    }

    public static void WriteDateOrNull(Utf8JsonWriter w, string property, DateOnly? value)
    {
        if (value is { } date) w.WriteString(property, date.ToString(DateFormat));
        else w.WriteNull(property);
    }

    // ---- Authored clock times ----

    public static TimeOnly ReadClockTime(JsonElement e) => TimeOnly.ParseExact(e.GetString()!, ClockTimeFormat);

    public static TimeOnly? ReadClockTimeOrNull(JsonElement parent, string property)
    {
        var value = parent.GetProperty(property);
        return value.ValueKind == JsonValueKind.Null ? null : ReadClockTime(value);
    }

    public static void WriteClockTime(Utf8JsonWriter w, string property, TimeOnly value) =>
        w.WriteString(property, value.ToString(ClockTimeFormat));

    public static void WriteClockTimeOrNull(Utf8JsonWriter w, string property, TimeOnly? value)
    {
        if (value is { } time) WriteClockTime(w, property, time);
        else w.WriteNull(property);
    }

    // ---- Recorded instants ----

    public static DateTimeOffset ReadInstant(JsonElement e) =>
        DateTimeOffset.Parse(e.GetString()!, null, DateTimeStyles.AssumeUniversal);

    public static DateTimeOffset? ReadInstantOrNull(JsonElement parent, string property)
    {
        var value = parent.GetProperty(property);
        return value.ValueKind == JsonValueKind.Null ? null : ReadInstant(value);
    }

    public static void WriteInstant(Utf8JsonWriter w, string property, DateTimeOffset value) =>
        w.WriteString(property, value.ToUniversalTime().ToString(InstantFormat));

    public static void WriteInstantOrNull(Utf8JsonWriter w, string property, DateTimeOffset? value)
    {
        if (value is { } instant) WriteInstant(w, property, instant);
        else w.WriteNull(property);
    }

    // ---- TagSet ----

    public static TagSet ReadTagSet(JsonElement e)
    {
        var dimensions = new Dictionary<DimensionId, IReadOnlyList<TagValue>>();
        foreach (var property in e.GetProperty("dimensions").EnumerateObject())
        {
            var dimensionId = new DimensionId(property.Name);
            dimensions[dimensionId] = property.Value.ValueKind == JsonValueKind.Array
                ? property.Value.EnumerateArray().Select(v => new TagValue(v.GetString()!)).ToArray()
                : [new TagValue(property.Value.GetString()!)];
        }

        var looseTags = e.GetProperty("looseTags").EnumerateArray()
            .Select(v => new LooseTag(v.GetString()!))
            .ToArray();

        return new TagSet(dimensions, looseTags);
    }

    /// <summary>Writes both the `dimensions` and `looseTags` properties on the current object.</summary>
    public static void WriteTagSet(Utf8JsonWriter w, TagSet tags)
    {
        w.WritePropertyName("dimensions");
        w.WriteStartObject();
        foreach (var (dimensionId, values) in tags.Dimensions)
        {
            w.WritePropertyName(dimensionId.Value);
            if (IsOrdinal(dimensionId) && values.Count == 1)
            {
                w.WriteStringValue(values[0].Value);
            }
            else
            {
                w.WriteStartArray();
                foreach (var value in values) w.WriteStringValue(value.Value);
                w.WriteEndArray();
            }
        }

        w.WriteEndObject();

        w.WritePropertyName("looseTags");
        w.WriteStartArray();
        foreach (var looseTag in tags.LooseTags) w.WriteStringValue(looseTag.Value);
        w.WriteEndArray();
    }

    private static bool IsOrdinal(DimensionId dimensionId) =>
        KnownDimensions.Default.Dimensions.FirstOrDefault(d => d.Id == dimensionId) is OrdinalDimension;

    // ---- Offset ----

    public static Offset? ReadOffsetOrNull(JsonElement parent, string property)
    {
        var value = parent.GetProperty(property);
        return value.ValueKind == JsonValueKind.Null ? null : ReadOffset(value);
    }

    private static Offset ReadOffset(JsonElement element)
    {
        var kind = element.GetProperty("kind").GetString();
        return kind switch
        {
            "before" => new BeforeOffset(element.GetProperty("n").GetInt32(), ReadOffsetUnit(element.GetProperty("unit"))),
            "lastWeekdayBefore" => new LastWeekdayBefore(ReadWeekday(element.GetProperty("weekday"))),
            _ => throw new JsonException($"Unknown Offset kind '{kind}'"),
        };
    }

    public static void WriteOffsetOrNull(Utf8JsonWriter w, string property, Offset? offset)
    {
        if (offset is null) { w.WriteNull(property); return; }

        w.WritePropertyName(property);
        WriteOffset(w, offset);
    }

    private static void WriteOffset(Utf8JsonWriter w, Offset offset)
    {
        w.WriteStartObject();
        switch (offset)
        {
            case BeforeOffset before:
                w.WriteString("kind", "before");
                w.WriteNumber("n", before.N);
                w.WriteString("unit", WriteOffsetUnit(before.Unit));
                break;
            case LastWeekdayBefore lastWeekdayBefore:
                w.WriteString("kind", "lastWeekdayBefore");
                w.WriteString("weekday", lastWeekdayBefore.Weekday.ToString().ToLowerInvariant());
                break;
            default:
                throw new JsonException($"Unknown Offset type '{offset.GetType()}'");
        }

        w.WriteEndObject();
    }

    /// <summary>Shared with Recurrence's `interval` rule, which also carries an <see cref="OffsetUnit"/>.</summary>
    public static OffsetUnit ReadOffsetUnit(JsonElement element) => element.GetString() switch
    {
        "days" => OffsetUnit.Days,
        "weeks" => OffsetUnit.Weeks,
        "months" => OffsetUnit.Months,
        var unit => throw new JsonException($"Unknown OffsetUnit '{unit}'"),
    };

    public static string WriteOffsetUnit(OffsetUnit unit) => unit switch
    {
        OffsetUnit.Days => "days",
        OffsetUnit.Weeks => "weeks",
        OffsetUnit.Months => "months",
        _ => throw new JsonException($"Unknown OffsetUnit '{unit}'"),
    };

    /// <summary>Shared with Recurrence's `weekly` rule, which also carries a list of weekdays.</summary>
    public static DayOfWeek ReadWeekday(JsonElement element) =>
        Enum.Parse<DayOfWeek>(element.GetString()!, ignoreCase: true);

    // ---- Availability Window (shared by day-templates.json and overrides.json) ----

    public static AvailabilityWindow ReadWindow(JsonElement e) =>
        new(
            new WindowId(e.GetProperty("id").GetString()!),
            e.GetProperty("name").GetString()!,
            ReadClockTime(e.GetProperty("start")),
            ReadClockTime(e.GetProperty("end")),
            ReadTagSet(e));

    public static void WriteWindow(Utf8JsonWriter w, AvailabilityWindow window)
    {
        w.WriteStartObject();
        w.WriteString("id", window.Id.Value);
        w.WriteString("name", window.Name);
        WriteClockTime(w, "start", window.Start);
        WriteClockTime(w, "end", window.End);
        WriteTagSet(w, window.Tags);
        w.WriteEndObject();
    }
}
