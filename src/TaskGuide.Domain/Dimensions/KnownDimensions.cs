using TaskGuide.Domain.Tags;

namespace TaskGuide.Domain.Dimensions;

/// <summary>
/// The registry as declared today. Adding a Dimension is a code change — and only a code
/// change: the editor's control is derived from the algebra, so nothing further is authored.
/// </summary>
public static class KnownDimensions
{
    public static DimensionId Location { get; } = new("location");
    public static DimensionId WithWhom { get; } = new("withWhom");
    public static DimensionId Weather { get; } = new("weather");
    public static DimensionId MentalEnergy { get; } = new("energy");
    public static DimensionId Duration { get; } = new("duration");

    /// <summary>
    /// Duration's buckets: 2 / 10 / 30 / 60 / Longer minutes. The one property with no safe
    /// default, which is why its absence <em>is</em> `Unprocessed`.
    /// </summary>
    public static IReadOnlyList<TagValue> DurationBuckets { get; } =
        [new("2"), new("10"), new("30"), new("60"), new("longer")];

    public static DimensionRegistry Default { get; } = new([
        new CategoricalDimension(Location, "Location", [new("home"), new("garage"), new("outside"), new("desk")]),
        new CategoricalDimension(WithWhom, "With whom", [new("sam"), new("ana"), new("carrie"), new("the kids")]),
        new CategoricalDimension(Weather, "Weather", [new("dry"), new("sunny")], WindowValueSource.Fetched),
        new OrdinalDimension(MentalEnergy, "Mental energy",
            [new("low"), new("medium"), new("high")],
            TaskDefault: new("low"), WindowDefault: new("low")),
        new OrdinalDimension(Duration, "Duration", DurationBuckets,
            TaskDefault: null,                 // required on the Task side; absence is `Unprocessed`
            WindowDefault: null,               // derived from the Window's length
            WindowSource: WindowValueSource.Derived),
    ]);
}
