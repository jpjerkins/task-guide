using System.Globalization;
using TaskGuide.Domain.Firing;

namespace TaskGuide.Application.Firing;

public static class LandingPageUrl
{
    public const string FallbackKey = "fallback";

    public static Uri For(Uri landingPage, DateOnly date, FireRow fire)
    {
        var key = fire.Kind == FireKind.Fallback
            ? FallbackKey
            : fire.WindowId?.Value ?? throw new ArgumentException("A non-fallback fire must carry a Window identity.", nameof(fire));
        var dateSegment = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return new Uri(landingPage, $"{dateSegment}/{Uri.EscapeDataString(key)}");
    }
}
