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
/// <para>
/// The optional <c>events</c> property is #153's two-armed absence: missing means
/// <see cref="DateOverride.Events"/> is <c>null</c> — the date's Events still come from the
/// Pattern — and present, including <c>[]</c>, means the date's own Events. Never written as an
/// explicit JSON <c>null</c>, which would be a third encoding of the same absence. This is the
/// choice ADR-0010's checklist for a new codec ("key it, decide which arm of absence applies,
/// wrap the boundary") asks every codec to make for itself.
/// </para>
/// </summary>
public static class OverrideCodec
{
    public static IReadOnlyList<DateOverride> Read(string json)
    {
        string? recordIdentity = null;
        return StoreCodecBoundary.Read(
            "overrides.json",
            "each date Override must satisfy the overrides.json schema",
            () => ReadCore(json, id => recordIdentity = id),
            () => recordIdentity);
    }

    private static IReadOnlyList<DateOverride> ReadCore(string json, Action<string?> identify)
    {
        using var document = JsonDocument.Parse(json);

        var overrides = new List<DateOverride>();

        foreach (var element in document.RootElement.EnumerateArray())
        {
            identify(null);
            var date = CodecPrimitives.ReadDate(element.GetProperty("date"));
            identify(date.ToString("yyyy-MM-dd"));

            var windows = element.GetProperty("windows").EnumerateArray()
                .Select(CodecPrimitives.ReadWindow)
                .ToList();

            // #153's two-armed absence: the property missing means the date's Events still come
            // from the Pattern (an Override written before #153); present — including an empty
            // array — means the date's own Events. TryGetProperty, not GetProperty, precisely
            // because absence is meaningful here.
            IReadOnlyList<Event>? events = element.TryGetProperty("events", out var eventsElement)
                ? eventsElement.EnumerateArray().Select(CodecPrimitives.ReadEvent).ToList()
                : null;

            if (events is not null)
            {
                RejectMismatchedEventDate(date, events);
            }

            overrides.Add(new DateOverride(date, windows, ReadUsedOrNull(element.GetProperty("used"))) { Events = events });
        }

        return overrides;
    }

    // The store file is a trust boundary and hand-editing it is an expected repair path
    // (ADR-0010: the operator's next act is to open the file and fix a row) — every Event
    // repeats its row's date with nothing else cross-checking it, so one wrong date in a
    // hand-edited or restored file would otherwise silently put an event on a day it does not
    // belong to.
    private static void RejectMismatchedEventDate(DateOnly rowDate, IReadOnlyList<Event> events)
    {
        var mismatched = events.FirstOrDefault(e => e.Date != rowDate);
        if (mismatched is null) return;

        throw new JsonException(
            $"Override event '{mismatched.Id.Value}' has date ({mismatched.Date:yyyy-MM-dd}) that " +
            $"does not match its row's date ({rowDate:yyyy-MM-dd}).");
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

            // Omitted, not `null`, when absent — a written `null` would be a third on-disk
            // encoding of the same thing `TryGetProperty`'s absence already means, and omitting
            // keeps every pre-#153 fixture row byte-identical.
            if (dateOverride.Events is { } events)
            {
                writer.WritePropertyName("events");
                writer.WriteStartArray();
                foreach (var @event in events) CodecPrimitives.WriteEvent(writer, @event);
                writer.WriteEndArray();
            }

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
