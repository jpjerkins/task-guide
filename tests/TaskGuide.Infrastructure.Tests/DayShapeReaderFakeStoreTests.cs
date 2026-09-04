using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;
using TaskGuide.Infrastructure.Storage;
using TaskGuide.TestSupport;
using Xunit;

namespace TaskGuide.Infrastructure.Tests;

/// <summary>
/// `tests/TEST-INVENTORY.md`, "Test support (#77)": the real end-to-end check for review finding
/// 1, run against the actual <see cref="DayShapeReader"/> rather than re-walking its three steps
/// by hand — <see cref="TaskGuide.Application.Tests.FakeStoreViewTests"/> carries that proxy
/// version because it cannot reference <c>TaskGuide.Infrastructure</c>.
/// </summary>
public sealed class DayShapeReaderFakeStoreTests
{
    [Fact]
    public void An_unseeded_FakeStore_handed_to_DayShapeReader_returns_a_usable_DayShape()
    {
        var store = new FakeStore();
        var reader = new DayShapeReader(store);
        var date = new DateOnly(2026, 9, 5);

        var shape = reader.For(date);

        Assert.Equal(date, shape.Date);
        Assert.False(shape.IsOverridden);
        Assert.Empty(shape.Windows);
        Assert.Empty(shape.Events);
    }

    /// <summary>#116 finding 1: a `DayTemplatesWrite` through `MutateAsync` re-points the
    /// builder's default Pattern at the first seeded template, so a store seeded with templates
    /// alone — via a real write, not just `WithDayTemplates` — still resolves end to end.</summary>
    [Fact]
    public async Task A_DayTemplatesWrite_through_MutateAsync_re_points_the_builders_default_Pattern_the_same_way()
    {
        var mine = new DayTemplate(new DayTemplateId("dt_mine"), "My day", [], []);
        var store = new FakeStore();
        await store.MutateAsync<Never>(
            _ => new StoreMutation([new DayTemplatesWrite([mine])]), CancellationToken.None);
        var reader = new DayShapeReader(store);
        var date = new DateOnly(2026, 9, 4);

        var shape = reader.For(date);

        Assert.Equal(date, shape.Date);
        Assert.False(shape.IsOverridden);
    }
}
