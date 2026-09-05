using System.Text.Json;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Tasks;

namespace TaskGuide.Infrastructure.Storage;

/// <summary>
/// Reads and writes <c>tasks.json</c> byte-meaningfully against the golden store fixture
/// (`tests/TaskGuide.Storage.Tests/fixtures/data/README.md`): three date/time encodings, no
/// `status` field (#47 — Status is derived, never stored), camelCase property names.
/// </summary>
public static class TaskCodec
{
    private const string DateFormat = "yyyy-MM-dd";

    public static IReadOnlyList<TaskItem> Read(string json)
    {
        using var document = JsonDocument.Parse(json);

        var tasks = new List<TaskItem>();

        foreach (var element in document.RootElement.EnumerateArray())
        {
            var id = new TaskId(element.GetProperty("id").GetString()!);

            var task = new TaskItem(
                id,
                element.GetProperty("title").GetString()!,
                ReadNullableString(element, "notes"),
                CodecPrimitives.ReadTagSet(element),
                CodecPrimitives.ReadDateOrNull(element, "deadline"),
                ReadDefer(element, "defer"),
                CodecPrimitives.ReadDateOrNull(element, "postpone"),
                ReadRecurrence(element, "recurrence"),
                CodecPrimitives.ReadInstant(element.GetProperty("createdAt")));

            tasks.Add(task);
        }

        return tasks;
    }

    public static void Write(Utf8JsonWriter writer, IReadOnlyList<TaskItem> tasks)
    {
        writer.WriteStartArray();

        foreach (var task in tasks)
        {
            writer.WriteStartObject();

            writer.WriteString("id", task.Id.Value);
            writer.WriteString("title", task.Title);
            if (task.Notes is null) writer.WriteNull("notes"); else writer.WriteString("notes", task.Notes);

            CodecPrimitives.WriteTagSet(writer, task.Tags);

            CodecPrimitives.WriteDateOrNull(writer, "deadline", task.Deadline);
            WriteDeferOrNull(writer, "defer", task.Defer);
            CodecPrimitives.WriteDateOrNull(writer, "postpone", task.Postpone);
            WriteRecurrenceOrNull(writer, "recurrence", task.Recurrence);

            CodecPrimitives.WriteInstant(writer, "createdAt", task.CreatedAt);

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static string? ReadNullableString(JsonElement element, string property)
    {
        var value = element.GetProperty(property);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetString();
    }

    private static Defer? ReadDefer(JsonElement element, string property)
    {
        var value = element.GetProperty(property);
        if (value.ValueKind == JsonValueKind.Null) return null;

        var kind = value.GetProperty("kind").GetString();
        return kind switch
        {
            "absolute" => new AbsoluteDefer(CodecPrimitives.ReadDate(value.GetProperty("date"))),
            "offset" => new OffsetDefer(CodecPrimitives.ReadOffsetOrNull(value, "offset")!),
            _ => throw new JsonException($"Unknown Defer kind '{kind}'"),
        };
    }

    private static void WriteDeferOrNull(Utf8JsonWriter writer, string property, Defer? defer)
    {
        if (defer is null) { writer.WriteNull(property); return; }

        writer.WritePropertyName(property);
        writer.WriteStartObject();
        defer.Switch(
            absolute =>
            {
                writer.WriteString("kind", "absolute");
                CodecPrimitives.WriteDateOrNull(writer, "date", absolute.Date);
            },
            offset =>
            {
                writer.WriteString("kind", "offset");
                CodecPrimitives.WriteOffsetOrNull(writer, "offset", offset.Offset);
            });

        writer.WriteEndObject();
    }

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
                ? CodecPrimitives.ReadDate(firstDueElement)
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
                element.GetProperty("weekdays").EnumerateArray().Select(CodecPrimitives.ReadWeekday).ToArray()),
            "monthly" => new MonthlyOnDayOfMonth(element.GetProperty("dayOfMonth").GetInt32()),
            "yearly" => new YearlyOn(element.GetProperty("month").GetInt32(), element.GetProperty("day").GetInt32()),
            "interval" => new IntervalSinceCompletion(
                element.GetProperty("n").GetInt32(), CodecPrimitives.ReadOffsetUnit(element.GetProperty("unit"))),
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
                writer.WriteString("unit", CodecPrimitives.WriteOffsetUnit(intervalSinceCompletion.Unit));
                break;
            default:
                throw new JsonException($"Unknown RecurrenceRule type '{rule.GetType()}'");
        }

        writer.WriteEndObject();
    }
}
