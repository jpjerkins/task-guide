using System.Text.Json;
using System.Text.Json.Nodes;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Firing;
using TaskGuide.Infrastructure.Storage;
using Xunit;

namespace TaskGuide.Storage.Tests;

/// <summary>
/// Against `fixtures/data/fires`, the golden store Fire record contract.
/// </summary>
public sealed class FireCodecTests
{
    private static string FixtureJson(string fileName) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "tests", "TaskGuide.Storage.Tests", "fixtures", "data", "fires", fileName));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "task-guide.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not find repo root (task-guide.slnx) above " + AppContext.BaseDirectory);
    }

    private static string RoundTrip(DateOnly date, string json)
    {
        var (fires, extras) = FireCodec.Read(date, json);
        return RoundTrip(fires, extras);
    }

    private static string RoundTrip(
        DayFires fires,
        IReadOnlyDictionary<FireKey, IReadOnlyList<KeyValuePair<string, JsonElement>>> extras)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) FireCodec.Write(writer, fires, extras);
        buffer.Position = 0;
        using var reader = new StreamReader(buffer);
        return reader.ReadToEnd();
    }

    [Fact]
    public void A_fire_row_carries_the_Window_s_name_and_span_as_they_were()
    {
        var (fires, _) = FireCodec.Read(new DateOnly(2026, 8, 15), FixtureJson("2026-08-15.json"));

        var row = Assert.Single(fires.Rows, r => r.WindowId == new WindowId("w_01ARZ3NDEKTSV4RRFFQ69G5H02") && r.Kind.ToString() == "Window");

        Assert.Equal("Evening prep", row.WindowName);
        Assert.Equal(new TimeOnly(17, 30), row.WindowStart);
        Assert.Equal(new TimeOnly(18, 0), row.WindowEnd);
    }

    [Fact]
    public void Date_null_fallback_is_unique_per_day()
    {
        const string json = """
            [
              { "windowId": null, "kind": "fallback",
                "windowName": null, "windowStart": null, "windowEnd": null,
                "dueAt": null, "firedAt": "2026-08-15T16:00:01Z", "matched": null,
                "carried": "evt_01ARZ3NDEKTSV4RRFFQ69G5M00" },
              { "windowId": null, "kind": "fallback",
                "windowName": null, "windowStart": null, "windowEnd": null,
                "dueAt": null, "firedAt": "2026-08-15T16:03:01Z", "matched": null,
                "carried": "evt_01ARZ3NDEKTSV4RRFFQ69G5M01" }
            ]
            """;

        var ex = Assert.Throws<JsonException>(() => FireCodec.Read(new DateOnly(2026, 8, 15), json));
        Assert.Contains("2026-08-15", ex.Message);
        Assert.Contains("fallback", ex.Message);
        Assert.Contains("null", ex.Message);
    }

    [Fact]
    public void Two_fire_rows_differing_only_in_windowId_both_load()
    {
        const string json = """
            [
              { "windowId": "w_01ARZ3NDEKTSV4RRFFQ69G5H02", "kind": "window",
                "windowName": "Evening prep", "windowStart": "17:30", "windowEnd": "18:00",
                "dueAt": null, "firedAt": "2026-08-15T22:45:03Z", "matched": 4, "carried": null },
              { "windowId": "w_01ARZ3NDEKTSV4RRFFQ69G5H03", "kind": "window",
                "windowName": "Morning prep", "windowStart": "08:00", "windowEnd": "09:00",
                "dueAt": null, "firedAt": "2026-08-15T13:00:07Z", "matched": 2, "carried": null }
            ]
            """;

        var (fires, _) = FireCodec.Read(new DateOnly(2026, 8, 15), json);

        Assert.Equal(2, fires.Rows.Count);
        Assert.Single(fires.Rows, r => r.WindowId == new WindowId("w_01ARZ3NDEKTSV4RRFFQ69G5H02"));
        Assert.Single(fires.Rows, r => r.WindowId == new WindowId("w_01ARZ3NDEKTSV4RRFFQ69G5H03"));
    }

    [Fact]
    public void Two_fire_rows_sharing_windowId_and_kind_are_rejected_at_read_with_the_date_named()
    {
        const string json = """
            [
              { "windowId": "w_01ARZ3NDEKTSV4RRFFQ69G5H02", "kind": "window",
                "windowName": "Evening prep", "windowStart": "17:30", "windowEnd": "18:00",
                "dueAt": null, "firedAt": "2026-08-15T22:45:03Z", "matched": 4, "carried": null },
              { "windowId": "w_01ARZ3NDEKTSV4RRFFQ69G5H02", "kind": "window",
                "windowName": "Evening prep", "windowStart": "17:30", "windowEnd": "18:00",
                "dueAt": null, "firedAt": "2026-08-15T22:49:11Z", "matched": 1, "carried": null }
            ]
            """;

        var ex = Assert.Throws<JsonException>(() => FireCodec.Read(new DateOnly(2026, 8, 15), json));
        Assert.Contains("2026-08-15", ex.Message);
        Assert.Contains("w_01ARZ3NDEKTSV4RRFFQ69G5H02", ex.Message);
        Assert.Contains("window", ex.Message);
    }

    [Theory]
    [InlineData("window", FireKind.Window)]
    [InlineData("unconditional", FireKind.Unconditional)]
    [InlineData("snooze", FireKind.Snooze)]
    [InlineData("fallback", FireKind.Fallback)]
    public void Every_FireKind_round_trips_through_its_own_JSON_string(string kind, FireKind expected)
    {
        var json = $$"""
            [
              { "windowId": "w_01ARZ3NDEKTSV4RRFFQ69G5H02", "kind": "{{kind}}",
                "windowName": null, "windowStart": null, "windowEnd": null,
                "dueAt": null, "firedAt": "2026-08-15T22:45:03Z", "matched": null, "carried": null }
            ]
            """;

        var (fires, extras) = FireCodec.Read(new DateOnly(2026, 8, 15), json);
        Assert.Equal(expected, Assert.Single(fires.Rows).Kind);

        var written = RoundTrip(fires, extras);
        using var document = JsonDocument.Parse(written);
        Assert.Equal(kind, document.RootElement[0].GetProperty("kind").GetString());
    }

    [Fact]
    public void Fires_2026_08_15_json_round_trips_the_golden_store_unchanged()
    {
        var original = FixtureJson("2026-08-15.json");

        var (fires, extras) = FireCodec.Read(new DateOnly(2026, 8, 15), original);
        Assert.Equal(new DateOnly(2026, 8, 15), fires.Date);

        var written = RoundTrip(fires, extras);

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original), JsonNode.Parse(written)));
    }

    [Fact]
    public void DueAt_and_firedAt_round_trip_as_instants_while_windowStart_and_windowEnd_round_trip_as_clock_times_in_the_same_file()
    {
        var written = RoundTrip(new DateOnly(2026, 8, 15), FixtureJson("2026-08-15.json"));

        using var document = JsonDocument.Parse(written);
        var window = document.RootElement[0];
        var snooze = document.RootElement[1];

        Assert.Equal("2026-08-15T22:45:03Z", window.GetProperty("firedAt").GetString());
        Assert.Equal("2026-08-15T23:07:00Z", snooze.GetProperty("dueAt").GetString());
        Assert.Equal("17:30", window.GetProperty("windowStart").GetString());
        Assert.Equal("18:00", window.GetProperty("windowEnd").GetString());
    }

    [Fact]
    public void A_pending_Snooze_row_round_trips_with_a_null_firedAt_and_reads_IsPendingSnooze()
    {
        var original = FixtureJson("2026-08-15.json");
        var (fires, extras) = FireCodec.Read(new DateOnly(2026, 8, 15), original);

        var snooze = Assert.Single(fires.Rows, r => r.Kind == FireKind.Snooze);
        Assert.True(snooze.IsPendingSnooze);
        Assert.Null(snooze.FiredAt);

        var written = RoundTrip(fires, extras);
        using var document = JsonDocument.Parse(written);
        Assert.Equal(JsonValueKind.Null, document.RootElement[1].GetProperty("firedAt").ValueKind);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original), JsonNode.Parse(written)));
    }

    [Theory]
    [InlineData("2026-08-15.json", 2026, 8, 15)]
    [InlineData("not-a-fire.json", 0, 0, 0)]
    [InlineData("2026-08-15.txt", 0, 0, 0)]
    public void Fire_dates_are_read_from_fire_file_names_without_parsing_contents(string fileName, int year, int month, int day)
    {
        var date = FireCodec.DateFromFileName(fileName);

        if (year == 0)
        {
            Assert.Null(date);
        }
        else
        {
            var actual = Assert.IsType<DateOnly>(date);
            Assert.Equal(new DateOnly(year, month, day), actual);
            Assert.Equal(fileName, FireCodec.FileNameFor(actual));
        }
    }

    [Theory]
    [InlineData("2026-8-15.json")]
    [InlineData("2026-08-5.json")]
    [InlineData("2026/08/15.json")]
    [InlineData("2026-08-15T00:00:00.json")]
    [InlineData("08-15-2026.json")]
    [InlineData("2026-02-30.json")]
    public void A_fire_file_name_whose_date_is_not_exactly_yyyy_MM_dd_is_not_a_fire_file(string fileName)
    {
        Assert.Null(FireCodec.DateFromFileName(fileName));
    }

    [Fact]
    public void An_unknown_field_on_a_fire_row_survives_a_load_and_save_round_trip()
    {
        // Two rows differing only in kind: the extras channel is keyed on (windowId, kind), the
        // same pair the duplicate guard enforces, so the field must land back on its own row.
        const string json = """
            [
              { "windowId": "w_01ARZ3NDEKTSV4RRFFQ69G5H02", "kind": "window",
                "windowName": "Evening prep", "windowStart": "17:30", "windowEnd": "18:00",
                "dueAt": null, "firedAt": "2026-08-15T22:45:03Z", "matched": 4, "carried": null },
              { "windowId": "w_01ARZ3NDEKTSV4RRFFQ69G5H02", "kind": "snooze",
                "windowName": null, "windowStart": null, "windowEnd": null,
                "dueAt": "2026-08-15T23:07:00Z", "firedAt": null, "matched": null, "carried": null,
                "futureField": "keep me" }
            ]
            """;

        var (fires, extras) = FireCodec.Read(new DateOnly(2026, 8, 15), json);
        var written = RoundTrip(fires, extras);

        using var document = JsonDocument.Parse(written);
        Assert.False(document.RootElement[0].TryGetProperty("futureField", out _),
            "The unknown field belongs to the snooze row, not the window row.");
        Assert.Equal("keep me", document.RootElement[1].GetProperty("futureField").GetString());
    }

    [Fact]
    public void FireCodec_writes_no_status_property()
    {
        var written = RoundTrip(new DateOnly(2026, 8, 15), FixtureJson("2026-08-15.json"));

        using var document = JsonDocument.Parse(written);
        foreach (var row in document.RootElement.EnumerateArray())
        {
            CodecAssertions.NoStatusProperty(row);
        }
    }
}
