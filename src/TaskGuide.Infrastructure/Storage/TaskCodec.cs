using System.Text.Json;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;

namespace TaskGuide.Infrastructure.Storage;

/// <summary>
/// Reads and writes <c>tasks.json</c> byte-meaningfully against the golden store fixture
/// (`tests/TaskGuide.Storage.Tests/fixtures/data/README.md`): three date/time encodings, no
/// `status` field (#47 — Status is derived, never stored), camelCase property names.
/// </summary>
/// <remarks>
/// A property this binary does not know about is preserved verbatim on every field it did not
/// touch, keyed per Task — a newer binary's addition survives a load-and-save round trip
/// untouched rather than being silently dropped by an unfamiliar codec.
/// </remarks>
public static class TaskCodec
{
    private const string DateFormat = "yyyy-MM-dd";
    private const string InstantFormat = "yyyy-MM-ddTHH:mm:ssZ";

    private static readonly string[] KnownFields =
        ["id", "title", "notes", "dimensions", "looseTags", "deadline", "defer", "postpone", "recurrence", "createdAt"];

    public static (IReadOnlyList<TaskItem> Tasks, IReadOnlyDictionary<TaskId, IReadOnlyList<KeyValuePair<string, JsonElement>>> Extras) Read(string json)
    {
        using var document = JsonDocument.Parse(json);

        var tasks = new List<TaskItem>();
        var extras = new Dictionary<TaskId, IReadOnlyList<KeyValuePair<string, JsonElement>>>();

        foreach (var element in document.RootElement.EnumerateArray())
        {
            var id = new TaskId(element.GetProperty("id").GetString()!);

            var task = new TaskItem(
                id,
                element.GetProperty("title").GetString()!,
                ReadNullableString(element, "notes"),
                ReadTagSet(element),
                ReadDateOnly(element, "deadline"),
                ReadDefer(element, "defer"),
                ReadDateOnly(element, "postpone"),
                ReadRecurrence(element, "recurrence"),
                ReadInstant(element.GetProperty("createdAt")));

            tasks.Add(task);

            var extra = element.EnumerateObject()
                .Where(p => !KnownFields.Contains(p.Name))
                .Select(p => KeyValuePair.Create(p.Name, p.Value.Clone()))
                .ToList();

            if (extra.Count > 0) extras[id] = extra;
        }

        return (tasks, extras);
    }

    public static void Write(
        Utf8JsonWriter writer,
        IReadOnlyList<TaskItem> tasks,
        IReadOnlyDictionary<TaskId, IReadOnlyList<KeyValuePair<string, JsonElement>>> extras)
    {
        writer.WriteStartArray();

        foreach (var task in tasks)
        {
            writer.WriteStartObject();

            writer.WriteString("id", task.Id.Value);
            writer.WriteString("title", task.Title);
            if (task.Notes is null) writer.WriteNull("notes"); else writer.WriteString("notes", task.Notes);

            writer.WritePropertyName("dimensions");
            WriteDimensions(writer, task.Tags);

            writer.WritePropertyName("looseTags");
            writer.WriteStartArray();
            foreach (var looseTag in task.Tags.LooseTags) writer.WriteStringValue(looseTag.Value);
            writer.WriteEndArray();

            WriteDateOnlyOrNull(writer, "deadline", task.Deadline);
            WriteDeferOrNull(writer, "defer", task.Defer);
            WriteDateOnlyOrNull(writer, "postpone", task.Postpone);
            WriteRecurrenceOrNull(writer, "recurrence", task.Recurrence);

            writer.WriteString("createdAt", task.CreatedAt.ToUniversalTime().ToString(InstantFormat));

            if (extras.TryGetValue(task.Id, out var extra))
            {
                foreach (var (name, value) in extra)
                {
                    writer.WritePropertyName(name);
                    value.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static string? ReadNullableString(JsonElement element, string property)
    {
        var value = element.GetProperty(property);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetString();
    }

    private static DateOnly? ReadDateOnly(JsonElement element, string property)
    {
        var value = element.GetProperty(property);
        return value.ValueKind == JsonValueKind.Null ? null : DateOnly.ParseExact(value.GetString()!, DateFormat);
    }

    private static void WriteDateOnlyOrNull(Utf8JsonWriter writer, string property, DateOnly? value)
    {
        if (value is { } date) writer.WriteString(property, date.ToString(DateFormat));
        else writer.WriteNull(property);
    }

    private static DateTimeOffset ReadInstant(JsonElement element) =>
        DateTimeOffset.Parse(element.GetString()!, null, System.Globalization.DateTimeStyles.AssumeUniversal);

    private static TagSet ReadTagSet(JsonElement element)
    {
        var dimensions = new Dictionary<DimensionId, IReadOnlyList<TagValue>>();
        foreach (var property in element.GetProperty("dimensions").EnumerateObject())
        {
            var dimensionId = new DimensionId(property.Name);
            dimensions[dimensionId] = property.Value.ValueKind == JsonValueKind.Array
                ? property.Value.EnumerateArray().Select(v => new TagValue(v.GetString()!)).ToArray()
                : [new TagValue(property.Value.GetString()!)];
        }

        var looseTags = element.GetProperty("looseTags").EnumerateArray()
            .Select(v => new LooseTag(v.GetString()!))
            .ToArray();

        return new TagSet(dimensions, looseTags);
    }

    private static void WriteDimensions(Utf8JsonWriter writer, TagSet tags)
    {
        writer.WriteStartObject();
        foreach (var (dimensionId, values) in tags.Dimensions)
        {
            writer.WritePropertyName(dimensionId.Value);
            if (IsOrdinal(dimensionId) && values.Count == 1)
            {
                writer.WriteStringValue(values[0].Value);
            }
            else
            {
                writer.WriteStartArray();
                foreach (var value in values) writer.WriteStringValue(value.Value);
                writer.WriteEndArray();
            }
        }

        writer.WriteEndObject();
    }

    private static bool IsOrdinal(DimensionId dimensionId) =>
        KnownDimensions.Default.Dimensions.FirstOrDefault(d => d.Id == dimensionId) is OrdinalDimension;

    private static Defer? ReadDefer(JsonElement element, string property)
    {
        var value = element.GetProperty(property);
        if (value.ValueKind == JsonValueKind.Null) return null;

        var kind = value.GetProperty("kind").GetString();
        return kind switch
        {
            "absolute" => new AbsoluteDefer(DateOnly.ParseExact(value.GetProperty("date").GetString()!, DateFormat)),
            "offset" => new OffsetDefer(ReadOffset(value.GetProperty("offset"))),
            _ => throw new JsonException($"Unknown Defer kind '{kind}'"),
        };
    }

    private static void WriteDeferOrNull(Utf8JsonWriter writer, string property, Defer? defer)
    {
        if (defer is null) { writer.WriteNull(property); return; }

        writer.WritePropertyName(property);
        writer.WriteStartObject();
        switch (defer)
        {
            case AbsoluteDefer absolute:
                writer.WriteString("kind", "absolute");
                writer.WriteString("date", absolute.Date.ToString(DateFormat));
                break;
            case OffsetDefer offset:
                writer.WriteString("kind", "offset");
                writer.WritePropertyName("offset");
                WriteOffset(writer, offset.Offset);
                break;
            default:
                throw new JsonException($"Unknown Defer type '{defer.GetType()}'");
        }

        writer.WriteEndObject();
    }

    private static Offset ReadOffset(JsonElement element)
    {
        var kind = element.GetProperty("kind").GetString();
        return kind switch
        {
            "before" => new BeforeOffset(element.GetProperty("n").GetInt32(), ReadUnit(element.GetProperty("unit"))),
            "lastWeekdayBefore" => new LastWeekdayBefore(ReadWeekday(element.GetProperty("weekday"))),
            _ => throw new JsonException($"Unknown Offset kind '{kind}'"),
        };
    }

    private static void WriteOffset(Utf8JsonWriter writer, Offset offset)
    {
        writer.WriteStartObject();
        switch (offset)
        {
            case BeforeOffset before:
                writer.WriteString("kind", "before");
                writer.WriteNumber("n", before.N);
                writer.WriteString("unit", WriteUnit(before.Unit));
                break;
            case LastWeekdayBefore lastWeekdayBefore:
                writer.WriteString("kind", "lastWeekdayBefore");
                writer.WriteString("weekday", lastWeekdayBefore.Weekday.ToString().ToLowerInvariant());
                break;
            default:
                throw new JsonException($"Unknown Offset type '{offset.GetType()}'");
        }

        writer.WriteEndObject();
    }

    private static OffsetUnit ReadUnit(JsonElement element) => element.GetString() switch
    {
        "days" => OffsetUnit.Days,
        "weeks" => OffsetUnit.Weeks,
        "months" => OffsetUnit.Months,
        var unit => throw new JsonException($"Unknown OffsetUnit '{unit}'"),
    };

    private static string WriteUnit(OffsetUnit unit) => unit switch
    {
        OffsetUnit.Days => "days",
        OffsetUnit.Weeks => "weeks",
        OffsetUnit.Months => "months",
        _ => throw new JsonException($"Unknown OffsetUnit '{unit}'"),
    };

    private static DayOfWeek ReadWeekday(JsonElement element) =>
        Enum.Parse<DayOfWeek>(element.GetString()!, ignoreCase: true);

    private static Recurrence? ReadRecurrence(JsonElement element, string property)
    {
        var value = element.GetProperty(property);
        if (value.ValueKind == JsonValueKind.Null) return null;

        var anchor = value.GetProperty("anchor").GetString() switch
        {
            "calendar" => RecurrenceAnchor.Calendar,
            "completion" => RecurrenceAnchor.Completion,
            var anchorValue => throw new JsonException($"Unknown RecurrenceAnchor '{anchorValue}'"),
        };

        var rule = ReadRule(value.GetProperty("rule"));

        DateOnly? firstDue = value.TryGetProperty("firstDue", out var firstDueElement)
            && firstDueElement.ValueKind != JsonValueKind.Null
                ? DateOnly.ParseExact(firstDueElement.GetString()!, DateFormat)
                : null;

        return new Recurrence(anchor, rule, firstDue);
    }

    private static RecurrenceRule ReadRule(JsonElement element)
    {
        var kind = element.GetProperty("kind").GetString();
        return kind switch
        {
            "daily" => new EveryNDays(element.GetProperty("n").GetInt32()),
            "weekly" => new EveryNWeeksOn(
                element.GetProperty("n").GetInt32(),
                element.GetProperty("weekdays").EnumerateArray().Select(ReadWeekday).ToArray()),
            "monthly" => new MonthlyOnDayOfMonth(element.GetProperty("dayOfMonth").GetInt32()),
            "yearly" => new YearlyOn(element.GetProperty("month").GetInt32(), element.GetProperty("day").GetInt32()),
            "interval" => new IntervalSinceCompletion(
                element.GetProperty("n").GetInt32(), ReadUnit(element.GetProperty("unit"))),
            _ => throw new JsonException($"Unknown RecurrenceRule kind '{kind}'"),
        };
    }

    private static void WriteRecurrenceOrNull(Utf8JsonWriter writer, string property, Recurrence? recurrence)
    {
        if (recurrence is null) { writer.WriteNull(property); return; }

        writer.WritePropertyName(property);
        writer.WriteStartObject();

        writer.WriteString("anchor", recurrence.Anchor switch
        {
            RecurrenceAnchor.Calendar => "calendar",
            RecurrenceAnchor.Completion => "completion",
            _ => throw new JsonException($"Unknown RecurrenceAnchor '{recurrence.Anchor}'"),
        });

        writer.WritePropertyName("rule");
        WriteRule(writer, recurrence.Rule);

        if (recurrence.FirstDue is { } firstDue) writer.WriteString("firstDue", firstDue.ToString(DateFormat));

        writer.WriteEndObject();
    }

    private static void WriteRule(Utf8JsonWriter writer, RecurrenceRule rule)
    {
        writer.WriteStartObject();
        switch (rule)
        {
            case EveryNDays everyNDays:
                writer.WriteString("kind", "daily");
                writer.WriteNumber("n", everyNDays.N);
                break;
            case EveryNWeeksOn everyNWeeksOn:
                writer.WriteString("kind", "weekly");
                writer.WriteNumber("n", everyNWeeksOn.N);
                writer.WritePropertyName("weekdays");
                writer.WriteStartArray();
                foreach (var weekday in everyNWeeksOn.Weekdays) writer.WriteStringValue(weekday.ToString().ToLowerInvariant());
                writer.WriteEndArray();
                break;
            case MonthlyOnDayOfMonth monthlyOnDayOfMonth:
                writer.WriteString("kind", "monthly");
                writer.WriteNumber("dayOfMonth", monthlyOnDayOfMonth.DayOfMonth);
                break;
            case YearlyOn yearlyOn:
                writer.WriteString("kind", "yearly");
                writer.WriteNumber("month", yearlyOn.Month);
                writer.WriteNumber("day", yearlyOn.Day);
                break;
            case IntervalSinceCompletion intervalSinceCompletion:
                writer.WriteString("kind", "interval");
                writer.WriteNumber("n", intervalSinceCompletion.N);
                writer.WriteString("unit", WriteUnit(intervalSinceCompletion.Unit));
                break;
            default:
                throw new JsonException($"Unknown RecurrenceRule type '{rule.GetType()}'");
        }

        writer.WriteEndObject();
    }
}
