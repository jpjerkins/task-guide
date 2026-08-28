using TaskGuide.Domain.Tasks;
using Xunit;

namespace TaskGuide.Domain.Tests;

/// <summary>
/// `tests/TEST-INVENTORY.md`'s "Offset" section.
/// </summary>
public sealed class OffsetTests
{
    [Theory]
    [InlineData(3, OffsetUnit.Days, "2026-08-28", "2026-08-25")]
    [InlineData(2, OffsetUnit.Weeks, "2026-08-28", "2026-08-14")]
    [InlineData(1, OffsetUnit.Months, "2026-08-28", "2026-07-28")]
    public void N_days_weeks_months_before_resolves_against_its_anchor(int n, OffsetUnit unit, string anchorText, string expectedText)
    {
        var anchor = DateOnly.Parse(anchorText);
        var expected = DateOnly.Parse(expectedText);

        var offset = new BeforeOffset(n, unit);

        Assert.Equal(expected, offset.ResolveAgainst(anchor));
    }

    [Fact]
    public void The_last_Friday_strictly_before_a_Friday_anchor_resolves_to_the_previous_week()
    {
        var anchor = new DateOnly(2026, 8, 28); // a Friday
        var offset = new LastWeekdayBefore(DayOfWeek.Friday);

        var resolved = offset.ResolveAgainst(anchor);

        Assert.Equal(new DateOnly(2026, 8, 21), resolved); // the Friday before, not its own morning
    }

    [Fact]
    public void The_last_Friday_strictly_before_a_Saturday_anchor_resolves_to_the_day_before()
    {
        var anchor = new DateOnly(2026, 8, 29); // a Saturday
        var offset = new LastWeekdayBefore(DayOfWeek.Friday);

        var resolved = offset.ResolveAgainst(anchor);

        Assert.Equal(new DateOnly(2026, 8, 28), resolved);
    }

    [Fact]
    public void A_month_unit_offset_from_the_31st_lands_on_a_real_date()
    {
        var anchor = new DateOnly(2026, 3, 31);
        var offset = new BeforeOffset(1, OffsetUnit.Months);

        var resolved = offset.ResolveAgainst(anchor);

        // February 2026 has 28 days — must not throw and must not silently roll into March.
        Assert.Equal(new DateOnly(2026, 2, 28), resolved);
    }
}
