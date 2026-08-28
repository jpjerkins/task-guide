using System.Text.Json;
using System.Text.Json.Nodes;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Infrastructure.Storage;
using Xunit;

namespace TaskGuide.Storage.Tests;

/// <summary>
/// Against `fixtures/data`, the golden store (`tests/TEST-INVENTORY.md`'s "Sequential ·
/// TaskGuide.Storage.Tests" section). Exercises `patterns.json` and `overrides.json`.
/// </summary>
public sealed class ScheduleCodecTests
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

    private static string RoundTripPatterns(
        PatternBook book,
        IReadOnlyDictionary<PatternId, IReadOnlyList<KeyValuePair<string, JsonElement>>> extras,
        IReadOnlyList<KeyValuePair<string, JsonElement>> envelopeExtras)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) PatternCodec.Write(writer, book, extras, envelopeExtras);
        buffer.Position = 0;
        using var reader = new StreamReader(buffer);
        return reader.ReadToEnd();
    }

    private static string RoundTripOverrides(
        IReadOnlyList<DateOverride> overrides,
        IReadOnlyDictionary<DateOnly, IReadOnlyList<KeyValuePair<string, JsonElement>>> extras)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) OverrideCodec.Write(writer, overrides, extras);
        buffer.Position = 0;
        using var reader = new StreamReader(buffer);
        return reader.ReadToEnd();
    }

    // ---- patterns.json ----

    [Fact]
    public void Patterns_json_round_trips_the_golden_store_unchanged()
    {
        var original = FixtureJson("patterns.json");

        var (book, extras, envelopeExtras) = PatternCodec.Read(original);
        var written = RoundTripPatterns(book, extras, envelopeExtras);

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original), JsonNode.Parse(written)));
    }

    [Fact]
    public void A_pattern_s_seven_days_are_indexed_by_weekday_with_sunday_first()
    {
        var (book, _, _) = PatternCodec.Read(FixtureJson("patterns.json"));
        var schoolYear = Assert.Single(book.Patterns, p => p.Name == "School year");

        // Fixture order: Sun, Mon, Tue, Wed, Thu, Fri, Sat — Tuesday is the odd one out (G01).
        Assert.Equal(new DayTemplateId("dt_01ARZ3NDEKTSV4RRFFQ69G5G00"), schoolYear[DayOfWeek.Sunday]);
        Assert.Equal(new DayTemplateId("dt_01ARZ3NDEKTSV4RRFFQ69G5G00"), schoolYear[DayOfWeek.Monday]);
        Assert.Equal(new DayTemplateId("dt_01ARZ3NDEKTSV4RRFFQ69G5G01"), schoolYear[DayOfWeek.Tuesday]);
        Assert.Equal(new DayTemplateId("dt_01ARZ3NDEKTSV4RRFFQ69G5G00"), schoolYear[DayOfWeek.Wednesday]);
        Assert.Equal(new DayTemplateId("dt_01ARZ3NDEKTSV4RRFFQ69G5G00"), schoolYear[DayOfWeek.Saturday]);
    }

    [Fact]
    public void A_pattern_book_whose_days_array_is_not_seven_long_is_rejected_at_read_naming_the_pattern()
    {
        const string json = """
            { "activePatternId": "p_01ARZ3NDEKTSV4RRFFQ69G5K00",
              "patterns": [
                { "id": "p_01ARZ3NDEKTSV4RRFFQ69G5K00", "name": "Broken",
                  "days": ["dt_01ARZ3NDEKTSV4RRFFQ69G5G00", "dt_01ARZ3NDEKTSV4RRFFQ69G5G00"] }] }
            """;

        var ex = Assert.Throws<JsonException>(() => PatternCodec.Read(json));
        Assert.Contains("Broken", ex.Message);
    }

    [Fact]
    public void No_codec_writes_a_status_property_whatever_type_it_would_carry_PatternCodec()
    {
        var (book, extras, envelopeExtras) = PatternCodec.Read(FixtureJson("patterns.json"));
        var written = RoundTripPatterns(book, extras, envelopeExtras);

        using var document = JsonDocument.Parse(written);
        CodecAssertions.NoStatusProperty(document.RootElement);
        foreach (var pattern in document.RootElement.GetProperty("patterns").EnumerateArray())
        {
            CodecAssertions.NoStatusProperty(pattern);
        }
    }

    [Fact]
    public void An_unknown_field_on_a_pattern_survives_a_load_and_save_round_trip()
    {
        const string json = """
            { "activePatternId": "p_01ARZ3NDEKTSV4RRFFQ69G5K00",
              "patterns": [
                { "id": "p_01ARZ3NDEKTSV4RRFFQ69G5K00", "name": "School year",
                  "days": ["dt_01ARZ3NDEKTSV4RRFFQ69G5G00", "dt_01ARZ3NDEKTSV4RRFFQ69G5G00",
                           "dt_01ARZ3NDEKTSV4RRFFQ69G5G00", "dt_01ARZ3NDEKTSV4RRFFQ69G5G00",
                           "dt_01ARZ3NDEKTSV4RRFFQ69G5G00", "dt_01ARZ3NDEKTSV4RRFFQ69G5G00",
                           "dt_01ARZ3NDEKTSV4RRFFQ69G5G00"],
                  "futureField": "keep me" }] }
            """;

        var (book, extras, envelopeExtras) = PatternCodec.Read(json);
        var written = RoundTripPatterns(book, extras, envelopeExtras);

        using var document = JsonDocument.Parse(written);
        Assert.Equal("keep me", document.RootElement.GetProperty("patterns")[0].GetProperty("futureField").GetString());
    }

    [Fact]
    public void An_unknown_field_on_the_patterns_envelope_survives_a_load_and_save_round_trip()
    {
        // patterns.json is an object, so a newer version can add a second singleton fact beside
        // `activePatternId`. That level has its own channel, separate from the per-Pattern one.
        const string json = """
            { "activePatternId": "p_01ARZ3NDEKTSV4RRFFQ69G5K00",
              "futureEnvelopeField": "keep me too",
              "patterns": [
                { "id": "p_01ARZ3NDEKTSV4RRFFQ69G5K00", "name": "School year",
                  "days": ["dt_01ARZ3NDEKTSV4RRFFQ69G5G00", "dt_01ARZ3NDEKTSV4RRFFQ69G5G00",
                           "dt_01ARZ3NDEKTSV4RRFFQ69G5G00", "dt_01ARZ3NDEKTSV4RRFFQ69G5G00",
                           "dt_01ARZ3NDEKTSV4RRFFQ69G5G00", "dt_01ARZ3NDEKTSV4RRFFQ69G5G00",
                           "dt_01ARZ3NDEKTSV4RRFFQ69G5G00"] }] }
            """;

        var (book, extras, envelopeExtras) = PatternCodec.Read(json);
        var written = RoundTripPatterns(book, extras, envelopeExtras);

        using var document = JsonDocument.Parse(written);
        Assert.Equal("keep me too", document.RootElement.GetProperty("futureEnvelopeField").GetString());
        Assert.False(document.RootElement.GetProperty("patterns")[0].TryGetProperty("futureEnvelopeField", out _),
            "An envelope-level unknown field must not be copied onto a Pattern.");
    }

    // ---- overrides.json ----

    [Fact]
    public void Overrides_json_round_trips_the_golden_store_unchanged()
    {
        var original = FixtureJson("overrides.json");

        var (overrides, extras) = OverrideCodec.Read(original);
        var written = RoundTripOverrides(overrides, extras);

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original), JsonNode.Parse(written)));
    }

    [Fact]
    public void An_overrides_copy_preserves_each_windows_id()
    {
        var (overrides, _) = OverrideCodec.Read(FixtureJson("overrides.json"));
        var volleyball = Assert.Single(overrides, o => o.Date == new DateOnly(2026, 8, 15));

        var window = Assert.Single(volleyball.Windows);
        Assert.Equal(new WindowId("w_01ARZ3NDEKTSV4RRFFQ69G5H02"), window.Id);
    }

    [Fact]
    public void An_override_carries_its_used_record_with_the_template_name_as_it_was()
    {
        var (overrides, _) = OverrideCodec.Read(FixtureJson("overrides.json"));
        var volleyball = Assert.Single(overrides, o => o.Date == new DateOnly(2026, 8, 15));

        Assert.NotNull(volleyball.Used);
        Assert.Equal(new DayTemplateId("dt_01ARZ3NDEKTSV4RRFFQ69G5G01"), volleyball.Used!.TemplateId);
        Assert.Equal("Volleyball Tuesday", volleyball.Used.TemplateName);
    }

    [Fact]
    public void A_one_off_day_round_trips_with_a_null_used()
    {
        var (overrides, extras) = OverrideCodec.Read(FixtureJson("overrides.json"));
        var oneOff = Assert.Single(overrides, o => o.Date == new DateOnly(2026, 9, 11));
        Assert.True(oneOff.IsOneOffDay);
        Assert.Null(oneOff.Used);

        var written = RoundTripOverrides(overrides, extras);
        using var document = JsonDocument.Parse(written);
        var writtenOneOff = document.RootElement.EnumerateArray()
            .Single(e => CodecPrimitives.ReadDate(e.GetProperty("date")) == new DateOnly(2026, 9, 11));

        Assert.Equal(JsonValueKind.Null, writtenOneOff.GetProperty("used").ValueKind);
    }

    [Fact]
    public void No_codec_writes_a_status_property_whatever_type_it_would_carry_OverrideCodec()
    {
        var (overrides, extras) = OverrideCodec.Read(FixtureJson("overrides.json"));
        var written = RoundTripOverrides(overrides, extras);

        using var document = JsonDocument.Parse(written);
        foreach (var element in document.RootElement.EnumerateArray())
        {
            CodecAssertions.NoStatusProperty(element);
        }
    }

    [Fact]
    public void An_unknown_field_on_an_override_survives_a_load_and_save_round_trip()
    {
        const string json = """
            [
              { "date": "2026-08-15", "used": null, "windows": [], "futureField": "keep me" }
            ]
            """;

        var (overrides, extras) = OverrideCodec.Read(json);
        var written = RoundTripOverrides(overrides, extras);

        using var document = JsonDocument.Parse(written);
        Assert.Equal("keep me", document.RootElement[0].GetProperty("futureField").GetString());
    }
}
