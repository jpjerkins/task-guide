using TaskGuide.Domain.Time;
using Xunit;

namespace TaskGuide.Domain.Tests;

/// <summary>
/// `tests/TEST-INVENTORY.md`'s "Day boundary and clock-time resolution" section.
/// </summary>
public sealed class DayBoundaryTests
{
    private static readonly TimeZoneInfo Chicago = TimeZoneInfo.FindSystemTimeZoneById(DayBoundary.ZoneId);

    [Theory]
    [InlineData("2026-01-15", -6)] // winter, CST
    [InlineData("2026-07-15", -5)] // summer, CDT
    public void The_day_boundary_is_local_midnight_in_America_Chicago_everywhere(string dateText, int expectedOffsetHours)
    {
        var boundary = new DayBoundary(Chicago);
        var date = DateOnly.Parse(dateText);

        var endOfPreviousDay = boundary.EndOf(date.AddDays(-1));

        var expected = new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.FromHours(expectedOffsetHours));
        Assert.Equal(expected, endOfPreviousDay);
        Assert.Equal(date, boundary.DateOf(endOfPreviousDay));
    }

    [Fact]
    public void An_ambiguous_start_on_the_fall_back_day_resolves_to_the_first_occurrence()
    {
        var boundary = new DayBoundary(Chicago);
        var resolution = new ClockTimeResolution(boundary);
        var date = new DateOnly(2026, 11, 1); // 2a repeats as 1a-2a

        var resolved = resolution.Resolve(date, new TimeOnly(1, 30));

        // The first occurrence of 1:30a is still on daylight time (CDT, -05:00), before the fall back.
        var expected = new DateTimeOffset(2026, 11, 1, 1, 30, 0, TimeSpan.FromHours(-5));
        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void A_nonexistent_start_in_the_spring_gap_clamps_to_the_gaps_end()
    {
        var boundary = new DayBoundary(Chicago);
        var resolution = new ClockTimeResolution(boundary);
        var date = new DateOnly(2026, 3, 8); // 2a -> 3a never happens

        var resolved = resolution.Resolve(date, new TimeOnly(2, 30));

        var expected = new DateTimeOffset(2026, 3, 8, 3, 0, 0, TimeSpan.FromHours(-5));
        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void A_span_crossing_the_spring_transition_is_honestly_an_hour_shorter()
    {
        var boundary = new DayBoundary(Chicago);
        var resolution = new ClockTimeResolution(boundary);
        var date = new DateOnly(2026, 3, 8);

        var length = resolution.LengthOf(date, new TimeOnly(1, 0), new TimeOnly(3, 0));

        // Wall clock says two hours; only one hour of real time elapsed.
        Assert.Equal(TimeSpan.FromHours(1), length);
    }

    [Fact]
    public void A_span_crossing_the_fall_back_transition_is_honestly_an_hour_longer()
    {
        var boundary = new DayBoundary(Chicago);
        var resolution = new ClockTimeResolution(boundary);
        var date = new DateOnly(2026, 11, 1);

        var length = resolution.LengthOf(date, new TimeOnly(1, 0), new TimeOnly(3, 0));

        // Wall clock says two hours; three hours of real time elapsed because 1a-2a happened twice.
        Assert.Equal(TimeSpan.FromHours(3), length);
    }

    [Fact]
    public void A_Window_lying_entirely_inside_the_spring_gap_has_zero_length_and_does_not_fire()
    {
        var boundary = new DayBoundary(Chicago);
        var resolution = new ClockTimeResolution(boundary);
        var date = new DateOnly(2026, 3, 8);

        var length = resolution.LengthOf(date, new TimeOnly(2, 10), new TimeOnly(2, 50));

        Assert.Equal(TimeSpan.Zero, length);
    }

    [Fact]
    public void DateOf_converts_an_arbitrary_offset_instant_through_Chicago_before_naming_the_date()
    {
        var boundary = new DayBoundary(Chicago);

        // Chicago midnight starting 2026-06-15 (CDT, -05:00) is 2026-06-15T05:00:00Z. One hour
        // before that, expressed in UTC (not Chicago's own offset), is still the previous day in
        // Chicago — a check that only passes if `DateOf` actually converts through `Zone` rather
        // than reading the DateTime component of whatever offset the instant happened to carry.
        var utcInstant = new DateTimeOffset(2026, 6, 15, 4, 0, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2026, 6, 14), boundary.DateOf(utcInstant));
    }

    [Theory]
    [InlineData("2026-01-16")] // ordinary winter day
    [InlineData("2026-03-09")] // day after the spring transition
    [InlineData("2026-11-02")] // day after the fall transition
    public void Deadline_Defer_and_Postpone_resolve_at_the_day_boundary(string dateText)
    {
        var boundary = new DayBoundary(Chicago);
        var date = DateOnly.Parse(dateText);

        // `now >= Defer` (or Deadline, or Postpone) becomes true at local midnight starting that
        // date — the same instant `EndOf` of the day before names, whatever the season.
        var gate = boundary.EndOf(date.AddDays(-1));

        var offset = Chicago.GetUtcOffset(date.ToDateTime(TimeOnly.MinValue));
        var expected = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), offset);
        Assert.Equal(expected, gate);
        Assert.Equal(date, boundary.DateOf(gate));
        Assert.Equal(date.AddDays(-1), boundary.DateOf(gate.AddTicks(-1)));
    }
}
