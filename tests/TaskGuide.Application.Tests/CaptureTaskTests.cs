using TaskGuide.Application.Capture;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Notifications;
using TaskGuide.TestSupport;
using Xunit;

namespace TaskGuide.Application.Tests;

public sealed class CaptureTaskTests
{
    [Fact]
    public async Task Capture_without_one_produces_an_Unprocessed_Task()
    {
        var (capture, store, receipts) = MakeCapture();

        var task = await capture.ExecuteAsync(new CaptureTaskRequest("Sort the garage", null, "quick-task-shortcut"), DetailPage, CancellationToken.None);

        Assert.Empty(task.Tags.Dimensions);
        Assert.Equal("quick-task-shortcut", task.Source);
        Assert.Single(store.Read().Tasks);
        Assert.Single(receipts.Receipts);
    }

    [Fact]
    public async Task Raw_minutes_snap_up()
    {
        var (capture, _, _) = MakeCapture();

        var task = await capture.ExecuteAsync(new CaptureTaskRequest("Repair the gate", 45, "quick-task-shortcut"), DetailPage, CancellationToken.None);

        Assert.Equal("60", task.Tags.SingleOn(KnownDimensions.Duration)?.Value);
    }

    [Fact]
    public async Task No_capture_path_writes_Tags()
    {
        var (capture, _, _) = MakeCapture();

        var task = await capture.ExecuteAsync(new CaptureTaskRequest("Buy paint", 30, "smart-add-task-shortcut"), DetailPage, CancellationToken.None);

        Assert.Empty(task.Tags.LooseTags);
    }

    [Fact]
    public async Task Capture_from_any_of_the_three_Shortcuts_sends_a_Receipt()
    {
        foreach (var source in new[] { "quick-task-shortcut", "detailed-task-shortcut", "smart-add-task-shortcut" })
        {
            var (capture, _, receipts) = MakeCapture();

            await capture.ExecuteAsync(new CaptureTaskRequest("Take bins out", 2, source), DetailPage, CancellationToken.None);

            Assert.Single(receipts.Receipts);
        }
    }

    [Fact]
    public async Task In_app_capture_sends_none()
    {
        var (capture, _, receipts) = MakeCapture();

        await capture.ExecuteAsync(new CaptureTaskRequest("Book the dentist", 15, ReceiptPolicy.InAppSource), DetailPage, CancellationToken.None);

        Assert.Empty(receipts.Receipts);
    }

    [Fact]
    public async Task An_unrecognised_source_sends_a_Receipt()
    {
        var (capture, _, receipts) = MakeCapture();

        await capture.ExecuteAsync(new CaptureTaskRequest("Future integration", 30, "new-source"), DetailPage, CancellationToken.None);

        Assert.Single(receipts.Receipts);
    }

    private static (CaptureTask Capture, FakeStore Store, RecordingReceiptSender Receipts) MakeCapture()
    {
        var store = new FakeStore();
        var receipts = new RecordingReceiptSender();
        return (new CaptureTask(store, new TestIdMinter(), receipts, TimeProvider.System), store, receipts);
    }

    private static Uri DetailPage(TaskId id) => new($"https://taskguide.test/api/tasks/{id.Value}");

    private sealed class TestIdMinter : IIdMinter
    {
        public TaskId NextTaskId() => new("t_01ARZ3NDEKTSV4RRFFQ69G5FAV");
        public WindowId NextWindowId() => throw new NotSupportedException();
        public DayTemplateId NextDayTemplateId() => throw new NotSupportedException();
        public PatternId NextPatternId() => throw new NotSupportedException();
        public EventId NextEventId() => throw new NotSupportedException();
        public EventPrototypeId NextEventPrototypeId() => throw new NotSupportedException();
    }
}
