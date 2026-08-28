using TaskGuide.Domain.Tags;

namespace TaskGuide.Domain.Matching;

/// <summary>
/// Duration's two directions of bucket snapping. A Window's ceiling must not
/// over-promise, so it rounds toward the bucket that fits <b>inside</b> the resolved
/// length; a capture path's raw-minute estimate must not under-claim, so it rounds toward
/// the bucket the estimate needs <b>at least</b>. Same buckets, opposite direction — never
/// collapsed into one function (ADR-0007).
/// </summary>
/// <remarks>
/// Neither function owns a bucket list of its own — both take the ordinal Duration
/// Dimension's declared values (<c>KnownDimensions.DurationBuckets</c>) as a parameter, the
/// same way <c>Matcher</c> takes a <c>MatchContext</c> rather than reaching for a static.
/// A bucket's minute count is read from its own value string (<c>"30"</c> parses as 30), so
/// there is nothing here to fall out of step with the registry: a bucket renamed, resized or
/// reordered there changes what this derives from it, not a second literal restating it.
/// </remarks>
public static class DurationCeiling
{
    /// <summary>
    /// The declared buckets that name a minute count, each paired with that count — every
    /// bucket except the one whose value cannot parse as a number (<c>longer</c>).
    /// </summary>
    private static IEnumerable<(int Minutes, TagValue Value)> SizedBucketsOf(IReadOnlyList<TagValue> orderedBuckets) =>
        orderedBuckets
            .Where(bucket => int.TryParse(bucket.Value, out _))
            .Select(bucket => (int.Parse(bucket.Value), bucket));

    /// <summary>
    /// The one declared bucket with no minute count — "longer" names no length, so it is
    /// identified by what it is <em>not</em> (parseable as minutes) rather than by position.
    /// </summary>
    private static TagValue UnsizedBucketOf(IReadOnlyList<TagValue> orderedBuckets) =>
        orderedBuckets.Single(bucket => !int.TryParse(bucket.Value, out _));

    /// <summary>
    /// The Window's ceiling: the largest bucket that fits <b>inside</b> the resolved length — it
    /// rounds down. A 45-minute Window admits the 30 bucket and below, not 60. Past the largest
    /// sized bucket the ceiling is the <b>unsized</b> one: a 90-minute or four-hour Window
    /// derives <c>longer</c>, because <c>SnapUp</c> gives any estimate of 61+ minutes that same
    /// bucket and a Task there would otherwise be permanently unmatchable — <c>longer</c> ranks
    /// above every reachable window value, so it would count zero Opportunities and be badged an
    /// Orphan whose advertised repair (declare this Tag on some Window) cannot be performed,
    /// Duration's window side being derived and refusing an authored Tag.
    /// </summary>
    /// <remarks>
    /// The bucket is a transient conversion for the matching comparison only. A Window goes on
    /// storing its real span — <c>AvailabilityWindow</c>'s authored <c>Start</c>/<c>End</c> clock
    /// times — and nothing here is cached: the ceiling is re-derived per date from the resolved
    /// length, so an Override, an Event or a DST fold changes it the same day it changes the span.
    /// <para>
    /// A length shorter than the smallest bucket (2 minutes) is a degenerate Window — the
    /// spring-gap case that clamps to zero length and, per the Availability Window entry,
    /// never fires. Its ceiling is never consulted for matching, so this falls back to the
    /// smallest bucket rather than declaring a rule for a case matching never reaches.
    /// </para>
    /// </remarks>
    public static TagValue WindowCeiling(TimeSpan length, IReadOnlyList<TagValue> orderedBuckets)
    {
        var minutes = length.TotalMinutes;
        var sized = SizedBucketsOf(orderedBuckets).ToArray();

        if (minutes > sized[^1].Minutes) return UnsizedBucketOf(orderedBuckets);

        var ceiling = sized[0].Value;

        foreach (var bucket in sized)
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
    public static TagValue SnapUp(int rawMinutes, IReadOnlyList<TagValue> orderedBuckets)
    {
        foreach (var bucket in SizedBucketsOf(orderedBuckets))
        {
            if (rawMinutes <= bucket.Minutes) return bucket.Value;
        }

        return UnsizedBucketOf(orderedBuckets);
    }
}
