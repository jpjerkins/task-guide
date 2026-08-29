using System.Text.Json;
using System.Text.Json.Nodes;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.Infrastructure.Storage;
using Xunit;

namespace TaskGuide.Storage.Tests;

/// <summary>
/// Against `fixtures/data`, the golden store (`tests/TEST-INVENTORY.md`'s "Sequential ·
/// TaskGuide.Storage.Tests" section). Exercises `events.json` and `event-exceptions.json`.
/// </summary>
public sealed class EventCodecTests
{
    private static string FixtureJson(string fileName) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "tests", "TaskGuide.Storage.Tests", "fixtures", "data", fileName));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "task-guide.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not find repo root (task-guide.slnx) above " + AppContext.BaseDirectory);
    }

    private static string RoundTrip(IReadOnlyList<Event> events, IReadOnlyDictionary<EventId, IReadOnlyList<KeyValuePair<string, JsonElement>>> extras)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) EventCodec.Write(writer, events, extras);
        buffer.Position = 0;
        using var reader = new StreamReader(buffer);
        return reader.ReadToEnd();
    }

    private static string RoundTripExceptions(IReadOnlyList<EventException> exceptions)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) EventCodec.WriteExceptions(writer, exceptions);
        buffer.Position = 0;
        using var reader = new StreamReader(buffer);
        return reader.ReadToEnd();
    }

    [Fact]
    public void Events_json_round_trips_the_golden_store_unchanged()
    {
        var original = FixtureJson("events.json");

        var (events, extras) = EventCodec.Read(original);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) EventCodec.Write(writer, events, extras);

        buffer.Position = 0;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original), JsonNode.Parse(buffer)));
    }

    [Fact]
    public void An_event_s_loose_tags_survive_the_round_trip_and_are_what_a_derived_obligation_rule_reads()
    {
        var (events, extras) = EventCodec.Read(FixtureJson("events.json"));

        var flight = Assert.Single(events, e => e.Id == new EventId("evt_01ARZ3NDEKTSV4RRFFQ69G5M01"));
        Assert.Contains(new LooseTag("timeoff"), flight.Tags.LooseTags);

        var written = RoundTrip(events, extras);
        var (roundTripped, _) = EventCodec.Read(written);

        var roundTrippedFlight = Assert.Single(roundTripped, e => e.Id == new EventId("evt_01ARZ3NDEKTSV4RRFFQ69G5M01"));
        Assert.Contains(new LooseTag("timeoff"), roundTrippedFlight.Tags.LooseTags);
    }

    [Fact]
    public void Event_exceptions_json_round_trips_both_the_delete_row_and_the_edit_row()
    {
        var original = FixtureJson("event-exceptions.json");

        var exceptions = EventCodec.ReadExceptions(original);
        var written = RoundTripExceptions(exceptions);

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original), JsonNode.Parse(written)));
    }

    [Fact]
    public void An_event_exception_that_is_neither_a_delete_nor_an_edit_is_rejected_at_read_naming_its_date()
    {
        const string json = """
            [
              { "date": "2026-08-18", "prototypeId": "ep_01ARZ3NDEKTSV4RRFFQ69G5J00", "deleted": false,
                "name": null, "start": null, "end": null }
            ]
            """;

        var ex = Assert.Throws<JsonException>(() => EventCodec.ReadExceptions(json));
        Assert.Contains("2026-08-18", ex.Message);
    }

    [Fact]
    public void Two_event_exceptions_sharing_a_date_and_prototypeId_are_rejected_at_read_naming_both()
    {
        const string json = """
            [
              { "date": "2026-08-18", "prototypeId": "ep_01ARZ3NDEKTSV4RRFFQ69G5J00", "deleted": true,
                "name": null, "start": null, "end": null },
              { "date": "2026-08-18", "prototypeId": "ep_01ARZ3NDEKTSV4RRFFQ69G5J00", "deleted": false,
                "name": "Karate late", "start": "19:00", "end": "20:00" }
            ]
            """;

        var ex = Assert.Throws<JsonException>(() => EventCodec.ReadExceptions(json));
        Assert.Contains("2026-08-18", ex.Message);
        Assert.Contains("ep_01ARZ3NDEKTSV4RRFFQ69G5J00", ex.Message);
    }

    [Fact]
    public void An_event_s_absence_notice_round_trips_and_a_null_one_stays_null()
    {
        var (events, extras) = EventCodec.Read(FixtureJson("events.json"));

        var withNotice = events
            .Select((e, i) => i == 0
                ? e with { AbsenceNotice = new LastWeekdayBefore(DayOfWeek.Sunday) }
                : e)
            .ToList();

        var written = RoundTrip(withNotice, extras);
        var (roundTripped, _) = EventCodec.Read(written);

        var concert = Assert.Single(roundTripped, e => e.Id == new EventId("evt_01ARZ3NDEKTSV4RRFFQ69G5M00"));
        Assert.Equal(new LastWeekdayBefore(DayOfWeek.Sunday), concert.AbsenceNotice);

        var flight = Assert.Single(roundTripped, e => e.Id == new EventId("evt_01ARZ3NDEKTSV4RRFFQ69G5M01"));
        Assert.Null(flight.AbsenceNotice);
    }

    /// <summary>
    /// No codec writes a `status` property, whatever type it would carry (#47). Kept in this
    /// file rather than the shared `StatusPropertyTests.cs` — this lane's brief names only this
    /// file as a new test file to create; `StatusPropertyTests.cs` is out of lane (Constraint 8).
    /// </summary>
    [Fact]
    public void EventCodec_writes_no_status_property()
    {
        var (events, extras) = EventCodec.Read(FixtureJson("events.json"));
        var written = RoundTrip(events, extras);

        using var document = JsonDocument.Parse(written);
        CodecAssertions.NoStatusProperty(document.RootElement[0]);
    }
}
