using System.Text.Json;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.Infrastructure.Storage;
using Xunit;

namespace TaskGuide.Storage.Tests;

/// <summary>
/// No codec writes a `status` property, whatever type it would carry (#47 — owed from the last
/// phase's review: "nothing can write a Status" is asserted structurally over the domain
/// assembly today, so a codec emitting `"status": "active"` as a plain string would slip through
/// every other test). Covers <see cref="TaskCodec"/> and <see cref="DayTemplateCodec"/> — the
/// two codecs this lane owns. <see cref="CodecAssertions.NoStatusProperty"/> is a public helper
/// so Tasks 3–6 can apply the same check to their own codecs.
/// </summary>
public sealed class StatusPropertyTests
{
    [Fact]
    public void TaskCodec_writes_no_status_property()
    {
        var task = new TaskItem(
            new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5FAV"),
            "Fix the shelf bracket",
            null,
            TagSet.Empty,
            null,
            null,
            null,
            null,
            new DateTimeOffset(2026, 8, 15, 14, 2, 11, TimeSpan.Zero));

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            TaskCodec.Write(writer, [task]);
        }

        buffer.Position = 0;
        using var document = JsonDocument.Parse(buffer);
        CodecAssertions.NoStatusProperty(document.RootElement[0]);
    }

    [Fact]
    public void DayTemplateCodec_writes_no_status_property()
    {
        var template = new DayTemplate(
            new DayTemplateId("dt_01ARZ3NDEKTSV4RRFFQ69G5G00"),
            "Ordinary weekday",
            [],
            []);

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            DayTemplateCodec.Write(writer, [template]);
        }

        buffer.Position = 0;
        using var document = JsonDocument.Parse(buffer);
        CodecAssertions.NoStatusProperty(document.RootElement[0]);
    }
}
