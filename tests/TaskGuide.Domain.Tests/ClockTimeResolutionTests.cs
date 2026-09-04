using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Time;
using Xunit;

namespace TaskGuide.Domain.Tests;

/// <summary>
/// `tests/TEST-INVENTORY.md`'s "Day boundary and clock-time resolution" section.
/// </summary>
public sealed class ClockTimeResolutionTests
{
    private static readonly DayBoundary Boundary = new(TimeZoneInfo.FindSystemTimeZoneById(DayBoundary.ZoneId));
    private static readonly ClockTimeResolution Resolution = new(Boundary);

    private static AvailabilityWindow Window(TimeOnly start, TimeOnly end) =>
        new(new WindowId("w_test"), "Test", start, end, TagSet.Empty);

    [Fact]
    public void An_ordinary_Window_resolves_to_the_two_instants_Resolve_would_give()
    {
        var date = new DateOnly(2026, 8, 27); // an ordinary Chicago day, no DST transition
        var window = Window(new TimeOnly(14, 0), new TimeOnly(15, 0));

        var resolved = Resolution.ResolveWindow(date, window);

        Assert.NotNull(resolved);
        Assert.Equal(window, resolved.Window);
        Assert.Equal(Resolution.Resolve(date, window.Start), resolved.Start);
        Assert.Equal(Resolution.Resolve(date, window.End), resolved.End);
    }

    [Fact]
    public void A_Window_entirely_inside_the_spring_gap_returns_null()
    {
        var date = new DateOnly(2026, 3, 8); // 2a -> 3a never happens
        var window = Window(new TimeOnly(2, 10), new TimeOnly(2, 50));

        Assert.Null(Resolution.ResolveWindow(date, window));
    }

    [Fact]
    public void A_Window_merely_crossing_the_spring_transition_still_resolves_an_hour_shorter()
    {
        var date = new DateOnly(2026, 3, 8);
        var window = Window(new TimeOnly(1, 0), new TimeOnly(3, 0));

        var resolved = Resolution.ResolveWindow(date, window);

        Assert.NotNull(resolved);
        Assert.Equal(TimeSpan.FromHours(1), resolved.End - resolved.Start);
    }
}
