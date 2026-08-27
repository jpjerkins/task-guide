using TaskGuide.Domain.Tags;

namespace TaskGuide.Domain.Matching;

/// <summary>
/// Duration's two directions of bucket snapping. A Window's ceiling must not
/// over-promise, so it rounds toward the bucket that fits <b>inside</b> the resolved
/// length; a capture path's raw-minute estimate must not under-claim, so it rounds toward
/// the bucket the estimate needs <b>at least</b>. Same buckets, opposite direction — never
/// collapsed into one function (ADR-0007).
/// </summary>
public static class DurationCeiling
{
    private static readonly (int Minutes, TagValue Value)[] SizedBuckets =
    [
        (2, new TagValue("2")),
        (10, new TagValue("10")),
        (30, new TagValue("30")),
        (60, new TagValue("60")),
    ];

    private static readonly TagValue Longer = new("longer");

    /// <summary>
    /// The Window's ceiling: the largest sized bucket that fits <b>inside</b> the resolved
    /// length — it rounds down. A 45-minute Window admits the 30 bucket and below, not 60.
    /// There is no window-side <c>longer</c> ceiling: "longer" names no length to fit inside,
    /// so a Window's promise is capped at the largest sized bucket, 60.
    /// </summary>
    /// <remarks>
    /// A length shorter than the smallest bucket (2 minutes) is a degenerate Window — the
    /// spring-gap case that clamps to zero length and, per the Availability Window entry,
    /// never fires. Its ceiling is never consulted for matching, so this falls back to the
    /// smallest bucket rather than declaring a rule for a case matching never reaches.
    /// </remarks>
    public static TagValue WindowCeiling(TimeSpan length)
    {
        var minutes = length.TotalMinutes;
        var ceiling = SizedBuckets[0].Value;

        foreach (var bucket in SizedBuckets)
        {
            if (bucket.Minutes <= minutes) ceiling = bucket.Value;
        }

        return ceiling;
    }

    /// <summary>
    /// A capture path's raw-minute estimate snaps <b>up</b> to the next bucket — the opposite
    /// direction from the Window's ceiling. An estimate must not under-claim: 45 minutes snaps
    /// to 60, and anything past the largest sized bucket (61+) snaps to <c>longer</c>.
    /// </summary>
    public static TagValue SnapUp(int rawMinutes)
    {
        foreach (var bucket in SizedBuckets)
        {
            if (rawMinutes <= bucket.Minutes) return bucket.Value;
        }

        return Longer;
    }
}
