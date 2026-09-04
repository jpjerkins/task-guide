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
}
