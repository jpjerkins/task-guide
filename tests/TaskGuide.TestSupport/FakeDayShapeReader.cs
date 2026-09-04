using TaskGuide.Domain.Schedule;

namespace TaskGuide.TestSupport;

/// <summary>
/// Seeded <see cref="DayShape"/>s by date; an unseeded date returns an empty shape and every read
/// is recorded. Reading a day's shape must write nothing, so this holds no store.
/// </summary>
public sealed class FakeDayShapeReader : IDayShapeReader
{
    public List<DateOnly> ReadDates { get; } = [];
    private readonly Dictionary<DateOnly, DayShape> _shapes = [];

    public void Seed(DateOnly date, DayShape shape) => _shapes[date] = shape;

    public DayShape For(DateOnly date)
    {
        ReadDates.Add(date);
        return _shapes.TryGetValue(date, out var shape) ? shape : new DayShape(date, [], [], IsOverridden: false);
    }
}
