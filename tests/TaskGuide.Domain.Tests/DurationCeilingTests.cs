using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Matching;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Time;
using Xunit;

namespace TaskGuide.Domain.Tests;

/// <summary>
/// `tests/TEST-INVENTORY.md`'s "Duration as a derived ceiling" section.
/// </summary>
public sealed class DurationCeilingTests
{
    private static readonly DayBoundary Boundary = new(TimeZoneInfo.FindSystemTimeZoneById(DayBoundary.ZoneId));
    private static readonly ClockTimeResolution Resolution = new(Boundary);
    private static readonly WindowId WId = new("w_duration");
    private static readonly DateOnly Date = new(2026, 8, 27); // an ordinary Chicago day, no DST transition

    private static readonly IReadOnlyList<TagValue> Buckets = KnownDimensions.DurationBuckets;

    [Fact]
    public void A_45_minute_Window_admits_the_30_bucket_and_below_and_not_60()
    {
        var length = Resolution.LengthOf(Date, new TimeOnly(9, 0), new TimeOnly(9, 45));

        var ceiling = DurationCeiling.WindowCeiling(length, Buckets);

        Assert.Equal(new TagValue("30"), ceiling);
        Assert.NotEqual(new TagValue("60"), ceiling);
    }

    [Fact]
    public void A_Windows_ceiling_is_derived_from_its_length_and_cannot_be_authored()
    {
        // Stuff a Duration Tag directly onto the Window's own Tags — there is no editor path to
        // do this, but nothing stops the record from carrying it. The ceiling must ignore it.
        var authoredDuration = new TagSet(
            new Dictionary<DimensionId, IReadOnlyList<TagValue>>
            {
                [KnownDimensions.Duration] = [new TagValue("60")],
            },
            Array.Empty<LooseTag>());

        var window = new AvailabilityWindow(WId, "Some window", new TimeOnly(9, 0), new TimeOnly(9, 45), authoredDuration);

        var ceiling = window.DurationCeiling(Date, Resolution, Buckets);

        Assert.Equal(new TagValue("30"), ceiling); // from the 45-minute length, not the authored "60" Tag
    }

    [Fact]
    public void A_60_minute_Window_admits_the_60_bucket_exactly()
    {
        var length = Resolution.LengthOf(Date, new TimeOnly(9, 0), new TimeOnly(10, 0));

        var ceiling = DurationCeiling.WindowCeiling(length, Buckets);

        Assert.Equal(new TagValue("60"), ceiling);
    }

    [Fact]
    public void Sixty_minutes_snaps_to_the_60_bucket_not_longer()
    {
        var bucket = DurationCeiling.SnapUp(60, Buckets);

        Assert.Equal(new TagValue("60"), bucket);
    }

    [Fact]
    public void Raw_minutes_from_a_capture_path_snap_up_to_the_next_bucket()
    {
        var bucket = DurationCeiling.SnapUp(45, Buckets);

        Assert.Equal(new TagValue("60"), bucket);
    }

    [Fact]
    public void Sixty_one_minutes_snaps_to_longer()
    {
        var bucket = DurationCeiling.SnapUp(61, Buckets);

        Assert.Equal(new TagValue("longer"), bucket);
    }

    /// <summary>
    /// The one test that fails if `DurationCeiling`'s bucket derivation and
    /// `KnownDimensions.DurationBuckets` ever disagree: it drives both functions off every
    /// numeric bucket the registry actually declares, rather than off a number restated here.
    /// A registry bucket renamed, reordered, or resized changes what this test expects too,
    /// because it reads the expectation from the same list it feeds the function under test.
    /// </summary>
    [Theory]
    [InlineData("2")]
    [InlineData("10")]
    [InlineData("30")]
    [InlineData("60")]
    public void Duration_ceiling_derives_its_bucket_minutes_from_KnownDimensions_DurationBuckets_not_a_private_copy(string bucket)
    {
        var minutes = int.Parse(bucket);
        var expected = new TagValue(bucket);

        Assert.Equal(expected, DurationCeiling.WindowCeiling(TimeSpan.FromMinutes(minutes), Buckets));
        Assert.Equal(expected, DurationCeiling.SnapUp(minutes, Buckets));
    }
}
