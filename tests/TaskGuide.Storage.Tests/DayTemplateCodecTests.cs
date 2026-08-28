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
/// TaskGuide.Storage.Tests" section). Exercises `day-templates.json` and the codec primitives
/// it shares with `TaskCodec`.
/// </summary>
public sealed class DayTemplateCodecTests
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

    private static string RoundTrip(IReadOnlyList<DayTemplate> templates, IReadOnlyDictionary<DayTemplateId, IReadOnlyList<KeyValuePair<string, JsonElement>>> extras)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) DayTemplateCodec.Write(writer, templates, extras);
        buffer.Position = 0;
        using var reader = new StreamReader(buffer);
        return reader.ReadToEnd();
    }

    [Fact]
    public void Day_templates_json_round_trips_the_golden_store_unchanged()
    {
        var original = FixtureJson("day-templates.json");

        var (templates, extras) = DayTemplateCodec.Read(original);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) DayTemplateCodec.Write(writer, templates, extras);

        buffer.Position = 0;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original), JsonNode.Parse(buffer)));
    }

    [Fact]
    public void A_window_s_start_and_end_round_trip_as_authored_clock_times_never_as_instants()
    {
        var (templates, extras) = DayTemplateCodec.Read(FixtureJson("day-templates.json"));
        var written = RoundTrip(templates, extras);

        using var document = JsonDocument.Parse(written);
        var firstWindow = document.RootElement[0].GetProperty("windows")[0];

        Assert.Equal("08:00", firstWindow.GetProperty("start").GetString());
        Assert.Equal("08:30", firstWindow.GetProperty("end").GetString());
    }

    [Fact]
    public void An_event_prototype_s_absence_notice_offset_round_trips_and_a_null_one_stays_null()
    {
        var (templates, extras) = DayTemplateCodec.Read(FixtureJson("day-templates.json"));

        var volleyballTuesday = Assert.Single(templates, t => t.Id == new DayTemplateId("dt_01ARZ3NDEKTSV4RRFFQ69G5G01"));
        var karate = Assert.Single(volleyballTuesday.EventPrototypes);
        Assert.Equal(new LastWeekdayBefore(DayOfWeek.Sunday), karate.AbsenceNotice);

        // Add a second template with a prototype carrying a null AbsenceNotice, and confirm it
        // stays null through a round trip.
        var noNotice = new EventPrototype(
            new EventPrototypeId("ep_01ARZ3NDEKTSV4RRFFQ69G5J01"),
            "Untitled",
            new TimeOnly(9, 0),
            new TimeOnly(10, 0),
            TagSet.Empty,
            null);
        var withExtra = templates
            .Append(new DayTemplate(new DayTemplateId("dt_01ARZ3NDEKTSV4RRFFQ69G5G03"), "Extra", [], [noNotice]))
            .ToList();

        var written = RoundTrip(withExtra, extras);
        var (roundTripped, _) = DayTemplateCodec.Read(written);

        var extraTemplate = Assert.Single(roundTripped, t => t.Id == new DayTemplateId("dt_01ARZ3NDEKTSV4RRFFQ69G5G03"));
        var roundTrippedPrototype = Assert.Single(extraTemplate.EventPrototypes);
        Assert.Null(roundTrippedPrototype.AbsenceNotice);
    }

    [Fact]
    public void An_unknown_field_on_a_day_template_survives_a_load_and_save_round_trip()
    {
        const string json = """
            [
              { "id": "dt_01ARZ3NDEKTSV4RRFFQ69G5G00", "name": "Ordinary weekday",
                "windows": [], "eventPrototypes": [], "futureField": "keep me" }
            ]
            """;

        var (templates, extras) = DayTemplateCodec.Read(json);
        var written = RoundTrip(templates, extras);

        using var document = JsonDocument.Parse(written);
        Assert.Equal("keep me", document.RootElement[0].GetProperty("futureField").GetString());
    }
}
