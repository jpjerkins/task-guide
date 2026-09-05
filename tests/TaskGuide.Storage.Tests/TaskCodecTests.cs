using System.Text.Json;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.Infrastructure.Storage;
using Xunit;

namespace TaskGuide.Storage.Tests;

/// <summary>
/// <see cref="TaskCodec"/>'s <c>Recurrence.Rule</c> writer binds its five lambdas to
/// <see cref="RecurrenceRule"/>'s type arguments by position (#72 review finding 1) — a
/// transposed pair of arms, or a reordering of the union's type arguments, would compile clean
/// and silently write the wrong <c>kind</c>. This is the safety net the old
/// <c>switch</c>/<c>case</c>'s <c>default: throw</c> used to provide.
/// </summary>
public sealed class TaskCodecTests
{
    private static TaskItem NewTask(Recurrence recurrence) => new(
        new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5H00"),
        "Recurring task",
        null,
        TagSet.Empty,
        null,
        null,
        null,
        recurrence,
        new DateTimeOffset(2026, 8, 15, 14, 2, 11, TimeSpan.Zero));

    private static string RoundTrip(TaskItem task)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) TaskCodec.Write(writer, [task]);
        buffer.Position = 0;
        using var reader = new StreamReader(buffer);
        return reader.ReadToEnd();
    }

    public static TheoryData<Recurrence, string> RecurrenceRuleKinds => new()
    {
        { new Recurrence(RecurrenceAnchor.Calendar, new EveryNDays(3), null), "daily" },
        { new Recurrence(RecurrenceAnchor.Calendar, new EveryNWeeksOn(1, [DayOfWeek.Tuesday]), null), "weekly" },
        { new Recurrence(RecurrenceAnchor.Calendar, new MonthlyOnDayOfMonth(15), null), "monthly" },
        { new Recurrence(RecurrenceAnchor.Calendar, new YearlyOn(6, 15), null), "yearly" },
        { new Recurrence(RecurrenceAnchor.Completion, new IntervalSinceCompletion(6, OffsetUnit.Months), null), "interval" },
    };

    [Theory]
    [MemberData(nameof(RecurrenceRuleKinds))]
    public void Every_RecurrenceRule_kind_round_trips_through_its_own_JSON_string(Recurrence recurrence, string kind)
    {
        var written = RoundTrip(NewTask(recurrence));

        using var document = JsonDocument.Parse(written);
        Assert.Equal(kind, document.RootElement[0].GetProperty("recurrence").GetProperty("rule").GetProperty("kind").GetString());

        var readBack = TaskCodec.Read(written);
        Assert.Equal(recurrence, Assert.Single(readBack).Recurrence);
    }
}
