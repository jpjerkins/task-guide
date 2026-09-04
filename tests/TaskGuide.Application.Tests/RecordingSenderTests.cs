using TaskGuide.Domain.Common;
using TaskGuide.Domain.Notifications;
using TaskGuide.TestSupport;
using Xunit;

namespace TaskGuide.Application.Tests;

/// <summary>
/// `tests/TEST-INVENTORY.md`, "Test support (#77)": the three recording senders share one shape —
/// record what they were handed, report success by default, and never throw for a configured
/// failure. Exercised once each, since the shape (not the port) is what is under test.
/// </summary>
public sealed class RecordingSenderTests
{
    [Fact]
    public async Task A_recording_sender_records_what_it_was_handed_and_reports_success()
    {
        var reminders = new RecordingReminderSender();
        var reminder = new Reminder(
            "Water the plants", "Evening", [], 0, [], new FooterCounts(0, 0, 0), [],
            new Uri("https://taskguide.example/today"), DateTimeOffset.UtcNow.AddHours(1));

        var accepted = await reminders.SendReminderAsync(reminder, CancellationToken.None);

        Assert.True(accepted);
        Assert.Same(reminder, Assert.Single(reminders.Reminders));
    }

    [Fact]
    public async Task A_recording_sender_reports_the_failure_it_was_configured_for_without_throwing()
    {
        var receipts = new RecordingReceiptSender();
        var receipt = new Receipt(new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5FAV"), "Water the plants", "15m", new Uri("https://taskguide.example/today"));
        receipts.FailNextSend();

        var accepted = await receipts.SendReceiptAsync(receipt, CancellationToken.None);

        Assert.False(accepted);
        Assert.Same(receipt, Assert.Single(receipts.Receipts));
    }
}
