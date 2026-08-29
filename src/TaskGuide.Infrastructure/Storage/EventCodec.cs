using System.Text.Json;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;

namespace TaskGuide.Infrastructure.Storage;

/// <summary>
/// Reads and writes <c>events.json</c> and <c>event-exceptions.json</c> byte-meaningfully against
/// the golden store fixture (`tests/TaskGuide.Storage.Tests/fixtures/data/README.md`).
/// </summary>
/// <remarks>
/// Per ADR-0001, an unknown field is dropped, not preserved — on either <c>events.json</c> or
/// <c>event-exceptions.json</c>. An exception is a small, fully-known row (`CONTEXT.md`, "Event
/// exception") keyed by (date, prototypeId), and a <c>deleted: false</c> row with all three of
/// name/start/end null is rejected at read — that shape is meaningless (neither a delete nor an
/// edit).
/// </remarks>
public static class EventCodec
{
    public static IReadOnlyList<Event> Read(string json)
    {
        using var document = JsonDocument.Parse(json);

        var events = new List<Event>();

        foreach (var element in document.RootElement.EnumerateArray())
        {
            var id = new EventId(element.GetProperty("id").GetString()!);

            events.Add(new Event(
                id,
                CodecPrimitives.ReadDate(element.GetProperty("date")),
                element.GetProperty("name").GetString()!,
                CodecPrimitives.ReadClockTime(element.GetProperty("start")),
                CodecPrimitives.ReadClockTime(element.GetProperty("end")),
                CodecPrimitives.ReadTagSet(element),
                CodecPrimitives.ReadOffsetOrNull(element, "absenceNotice")));
        }

        return events;
    }

    public static void Write(Utf8JsonWriter writer, IReadOnlyList<Event> events)
    {
        writer.WriteStartArray();

        foreach (var @event in events)
        {
            writer.WriteStartObject();

            writer.WriteString("id", @event.Id.Value);
            CodecPrimitives.WriteDateOrNull(writer, "date", @event.Date);
            writer.WriteString("name", @event.Name);
            CodecPrimitives.WriteClockTime(writer, "start", @event.Start);
            CodecPrimitives.WriteClockTime(writer, "end", @event.End);
            CodecPrimitives.WriteTagSet(writer, @event.Tags);
            CodecPrimitives.WriteOffsetOrNull(writer, "absenceNotice", @event.AbsenceNotice);

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    public static IReadOnlyList<EventException> ReadExceptions(string json)
    {
        using var document = JsonDocument.Parse(json);

        var exceptions = new List<EventException>();

        foreach (var element in document.RootElement.EnumerateArray())
        {
            var date = CodecPrimitives.ReadDate(element.GetProperty("date"));
            var prototypeId = new EventPrototypeId(element.GetProperty("prototypeId").GetString()!);
            var deleted = element.GetProperty("deleted").GetBoolean();
            var name = element.GetProperty("name").ValueKind == JsonValueKind.Null
                ? null
                : element.GetProperty("name").GetString();
            var start = CodecPrimitives.ReadClockTimeOrNull(element, "start");
            var end = CodecPrimitives.ReadClockTimeOrNull(element, "end");

            if (!deleted && name is null && start is null && end is null)
            {
                throw new JsonException(
                    $"Event exception on {date:yyyy-MM-dd} is neither a delete nor an edit: deleted is false but name, start and end are all null.");
            }

            exceptions.Add(new EventException(date, prototypeId, deleted, name, start, end));
        }

        RejectDuplicateKeys(exceptions);

        return exceptions;
    }

    private static void RejectDuplicateKeys(IReadOnlyList<EventException> exceptions)
    {
        var duplicate = exceptions
            .GroupBy(e => (e.Date, e.PrototypeId))
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is null) return;

        throw new JsonException(
            $"Event exception has duplicate key (date, prototypeId)=({duplicate.Key.Date:yyyy-MM-dd}, {duplicate.Key.PrototypeId.Value}).");
    }

    public static void WriteExceptions(Utf8JsonWriter writer, IReadOnlyList<EventException> exceptions)
    {
        writer.WriteStartArray();

        foreach (var exception in exceptions)
        {
            writer.WriteStartObject();

            CodecPrimitives.WriteDateOrNull(writer, "date", exception.Date);
            writer.WriteString("prototypeId", exception.PrototypeId.Value);
            writer.WriteBoolean("deleted", exception.Deleted);
            if (exception.Name is { } name) writer.WriteString("name", name); else writer.WriteNull("name");
            CodecPrimitives.WriteClockTimeOrNull(writer, "start", exception.Start);
            CodecPrimitives.WriteClockTimeOrNull(writer, "end", exception.End);

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }
}
