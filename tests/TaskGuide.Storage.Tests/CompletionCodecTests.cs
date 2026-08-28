using System.Text.Json;
using System.Text.Json.Nodes;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Tasks;
using TaskGuide.Infrastructure.Storage;
using Xunit;

namespace TaskGuide.Storage.Tests;

/// <summary>
/// Against `fixtures/data/completions`, the golden store completion-log contract.
/// </summary>
public sealed class CompletionCodecTests
{
    private static string FixtureJson(string fileName) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "tests", "TaskGuide.Storage.Tests", "fixtures", "data", "completions", fileName));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "task-guide.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not find repo root (task-guide.slnx) above " + AppContext.BaseDirectory);
    }

    private static string RoundTrip(TaskId taskId, string json)
    {
        var log = CompletionCodec.Read(taskId, json);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) CompletionCodec.Write(writer, log);
        buffer.Position = 0;
        using var reader = new StreamReader(buffer);
        return reader.ReadToEnd();
    }

    private static string RoundTrip(CompletionLog log)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) CompletionCodec.Write(writer, log);
        buffer.Position = 0;
        using var reader = new StreamReader(buffer);
        return reader.ReadToEnd();
    }

    private static string RoundTripDerived(string json)
    {
        var entries = CompletionCodec.ReadDerived(json);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) CompletionCodec.WriteDerived(writer, entries);
        buffer.Position = 0;
        using var reader = new StreamReader(buffer);
        return reader.ReadToEnd();
    }

    [Fact]
    public void A_completion_log_is_not_rewritten_when_its_Task_s_title_changes()
    {
        var original = FixtureJson("t_01ARZ3NDEKTSV4RRFFQ69G5FB0.json");

        var written = RoundTrip(new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5FB0"), original);

        using var document = JsonDocument.Parse(written);
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            Assert.False(entry.TryGetProperty("title", out _), "Completion log must not carry a Task title.");
            CodecAssertions.NoStatusProperty(entry);
        }

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original), JsonNode.Parse(written)));
    }

    [Theory]
    [InlineData("t_01ARZ3NDEKTSV4RRFFQ69G5FB0.json", "t_01ARZ3NDEKTSV4RRFFQ69G5FB0")]
    [InlineData("t_01ARZ3NDEKTSV4RRFFQ69G5FB3.json", "t_01ARZ3NDEKTSV4RRFFQ69G5FB3")]
    public void Each_completion_log_round_trips_the_golden_store_unchanged(string fileName, string taskId)
    {
        var original = FixtureJson(fileName);

        var written = RoundTrip(new TaskId(taskId), original);

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original), JsonNode.Parse(written)));
    }

    [Fact]
    public void A_one_off_Task_s_entry_round_trips_a_null_due()
    {
        var original = FixtureJson("t_01ARZ3NDEKTSV4RRFFQ69G5FB3.json");
        var log = CompletionCodec.Read(new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5FB3"), original);

        var entry = Assert.Single(log.Entries);
        Assert.Null(entry.Due);

        var written = RoundTrip(log);
        using var document = JsonDocument.Parse(written);
        Assert.Equal(JsonValueKind.Null, document.RootElement[0].GetProperty("due").ValueKind);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original), JsonNode.Parse(written)));

        var datedOriginal = FixtureJson("t_01ARZ3NDEKTSV4RRFFQ69G5FB0.json");
        var datedWritten = RoundTrip(new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5FB0"), datedOriginal);
        using var datedDocument = JsonDocument.Parse(datedWritten);
        Assert.Equal("2026-08-11", datedDocument.RootElement[0].GetProperty("due").GetString());
    }

    [Fact]
    public void Completions_derived_json_round_trips_keyed_on_ruleId_triggerId_due()
    {
        var original = FixtureJson("derived.json");

        var entries = CompletionCodec.ReadDerived(original);
        var entry = Assert.Single(entries);
        Assert.Equal(new RuleId("absence"), entry.RuleId);
        Assert.Equal("evt_01ARZ3NDEKTSV4RRFFQ69G5M01", entry.TriggerId);
        Assert.Equal(new DateOnly(2026, 9, 27), entry.Due);

        var written = RoundTripDerived(original);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original), JsonNode.Parse(written)));
    }

    [Fact]
    public void The_Task_id_comes_from_the_filename_so_a_log_file_carries_no_id_of_its_own()
    {
        var taskId = new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5FB0");
        var log = CompletionCodec.Read(taskId, FixtureJson(CompletionCodec.FileNameFor(taskId)));

        Assert.Equal(taskId, log.TaskId);

        var written = RoundTrip(log);
        using var document = JsonDocument.Parse(written);
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            Assert.False(entry.TryGetProperty("id", out _), "Completion log entries must not repeat the Task id.");
            Assert.False(entry.TryGetProperty("taskId", out _), "Completion log entries must not repeat the Task id.");
        }
    }

    [Fact]
    public void CompletionCodec_writes_no_status_property()
    {
        var written = RoundTrip(new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5FB0"), FixtureJson("t_01ARZ3NDEKTSV4RRFFQ69G5FB0.json"));

        using var document = JsonDocument.Parse(written);
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            CodecAssertions.NoStatusProperty(entry);
        }
    }
}
