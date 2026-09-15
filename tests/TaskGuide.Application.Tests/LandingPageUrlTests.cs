using TaskGuide.Application.Firing;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Schedule;
using Xunit;

namespace TaskGuide.Application.Tests;

public sealed class LandingPageUrlTests
{
    [Fact]
    public void A_window_fire_landing_URL_contains_its_date_and_window_identity()
    {
        var fire = new FireRow(new WindowId("w_evening"), FireKind.Window, "Evening", null, null, null, null, 1, null);

        var result = LandingPageUrl.For(new Uri("https://example.test/"), new DateOnly(2026, 9, 15), fire);

        Assert.Equal(new Uri("https://example.test/2026-09-15/w_evening"), result);
    }

    [Fact]
    public void A_fallback_fire_landing_URL_uses_the_literal_fallback_identity()
    {
        var fire = new FireRow(null, FireKind.Fallback, null, null, null, null, null, null, new EventId("evt_1"));

        var result = LandingPageUrl.For(new Uri("https://example.test/"), new DateOnly(2026, 9, 15), fire);

        Assert.Equal(new Uri("https://example.test/2026-09-15/fallback"), result);
    }

    [Fact]
    public void An_unknown_window_identity_is_preserved_in_the_landing_URL()
    {
        var fire = new FireRow(new WindowId("w_unknown"), FireKind.Window, null, null, null, null, null, 1, null);

        var result = LandingPageUrl.For(new Uri("https://example.test/"), new DateOnly(2026, 9, 15), fire);

        Assert.Equal("https://example.test/2026-09-15/w_unknown", result.AbsoluteUri);
    }

    [Fact]
    public void A_non_fallback_fire_without_a_window_identity_is_rejected_instead_of_becoming_fallback()
    {
        var fire = new FireRow(null, FireKind.Window, null, null, null, null, null, 1, null);

        Assert.Throws<ArgumentException>(() => LandingPageUrl.For(new Uri("https://example.test/"), new DateOnly(2026, 9, 15), fire));
    }
}
