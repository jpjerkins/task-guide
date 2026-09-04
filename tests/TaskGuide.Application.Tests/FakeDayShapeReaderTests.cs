using TaskGuide.Domain.Schedule;
using TaskGuide.TestSupport;
using Xunit;

namespace TaskGuide.Application.Tests;

/// <summary>
/// `tests/TEST-INVENTORY.md`, "Test support (#77)": <see cref="TaskGuide.TestSupport.FakeDayShapeReader"/>
/// reads an empty shape for an unseeded date and records which dates were read.
/// </summary>
public sealed class FakeDayShapeReaderTests
{
    [Fact]
    public void An_unseeded_date_reads_an_empty_DayShape_and_the_read_is_recorded()
    {
        var reader = new FakeDayShapeReader();
        var date = new DateOnly(2026, 9, 5);

        var shape = reader.For(date);

        Assert.Equal(date, shape.Date);
        Assert.Empty(shape.Windows);
        Assert.Empty(shape.Events);
        Assert.False(shape.IsOverridden);
        Assert.Equal(date, Assert.Single(reader.ReadDates));
    }
}
