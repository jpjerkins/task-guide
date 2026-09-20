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
        string? recordIdentity = null;
        return StoreCodecBoundary.Read(
            "events.json",
            "each Event must satisfy the events.json schema",
            () => ReadCore(json, id => recordIdentity = id),
            () => recordIdentity);
    }

    private static IReadOnlyList<Event> ReadCore(string json, Action<string?> identify)
    {
        using var document = JsonDocument.Parse(json);

        var events = new List<Event>();

        foreach (var element in document.RootElement.EnumerateArray())
        {
            identify(null);
            identify(element.GetProperty("id").GetString());

            events.Add(CodecPrimitives.ReadEvent(element));
        }

        return events;
    }

    public static void Write(Utf8JsonWriter writer, IReadOnlyList<Event> events)
    {
        writer.WriteStartArray();

        foreach (var @event in events) CodecPrimitives.WriteEvent(writer, @event);

        writer.WriteEndArray();
    }

    public static IReadOnlyList<EventException> ReadExceptions(string json)
    {
        string? recordIdentity = null;
        return StoreCodecBoundary.Read(
            "event-exceptions.json",
            "each Event exception must satisfy the event-exceptions.json schema",
            () => ReadExceptionsCore(json, id => recordIdentity = id),
            () => recordIdentity);
    }

    private static IReadOnlyList<EventException> ReadExceptionsCore(string json, Action<string?> identify)
    {
        using var document = JsonDocument.Parse(json);

        var exceptions = new List<EventException>();

        foreach (var element in document.RootElement.EnumerateArray())
        {
            identify(null);
            var date = CodecPrimitives.ReadDate(element.GetProperty("date"));
            var prototypeId = new EventPrototypeId(element.GetProperty("prototypeId").GetString()!);
            identify(EventExceptionIdentity(date, prototypeId));
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

        RejectDuplicateKeys(exceptions, identify);

        return exceptions;
    }

    private static void RejectDuplicateKeys(IReadOnlyList<EventException> exceptions, Action<string?> identify)
    {
        var duplicate = exceptions
            .GroupBy(e => (e.Date, e.PrototypeId))
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is null) return;

        identify(EventExceptionIdentity(duplicate.Key.Date, duplicate.Key.PrototypeId));
        throw new JsonException(
            $"Event exception has duplicate key (date, prototypeId)=({duplicate.Key.Date:yyyy-MM-dd}, {duplicate.Key.PrototypeId.Value}).");
    }

    private static string EventExceptionIdentity(DateOnly date, EventPrototypeId prototypeId) =>
        $"(date, prototypeId)=({date:yyyy-MM-dd}, {prototypeId.Value})";

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
