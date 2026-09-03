using TaskGuide.Domain.Firing;
using Xunit;

namespace TaskGuide.Domain.Tests;

/// <summary>
/// `tests/TEST-INVENTORY.md`'s "Day boundary and clock-time resolution" section.
/// </summary>
public sealed class FiringPolicyTests
{
    private static readonly DateTimeOffset Instant = new(2026, 3, 8, 3, 0, 0, TimeSpan.FromHours(-5));

    [Fact]
    public void A_Window_span_is_empty_when_the_end_equals_the_start()
    {
        Assert.True(FiringPolicy.IsWindowSpanEmpty(Instant, Instant));
    }

    [Fact]
    public void A_Window_span_is_empty_when_the_end_is_before_the_start()
    {
        Assert.True(FiringPolicy.IsWindowSpanEmpty(Instant, Instant.AddMinutes(-1)));
    }

    [Fact]
    public void A_Window_span_is_not_empty_when_the_end_is_after_the_start()
    {
        Assert.False(FiringPolicy.IsWindowSpanEmpty(Instant, Instant.AddMinutes(1)));
    }
}
