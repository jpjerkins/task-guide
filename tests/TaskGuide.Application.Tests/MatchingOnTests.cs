using TaskGuide.Application.RightNow;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Time;
using TaskGuide.TestSupport;
using Xunit;

namespace TaskGuide.Application.Tests;

public sealed class MatchingOnTests
{
    [Fact]
    public async Task PUT_api_right_now_matching_on_on_a_frozen_date_leaves_the_frozen_Events_intact()
    {
        var date = new DateOnly(2026, 12, 24);
        var window = Window();
        var frozenEvent = new Event(
            new EventId("evt_christmas_eve"),
            date,
            "Christmas Eve",
            new TimeOnly(18, 0),
            new TimeOnly(22, 0),
            TagSet.Empty,
            null);
        var store = new FakeStore(new FakeStoreViewBuilder()
            .WithOverrides([new DateOverride(date, [window], null) { Events = [frozenEvent] }])
            .Build());

        await ExecuteAsync(store, date, window.Id);

        var written = Assert.Single(store.Read().Overrides);
        Assert.NotNull(written.Events);
        Assert.Equal(frozenEvent, Assert.Single(written.Events));
    }

    [Fact]
    public async Task PUT_api_right_now_matching_on_on_a_blanked_date_leaves_its_empty_Events_empty_rather_than_reverting_to_the_Patterns()
    {
        var date = new DateOnly(2026, 12, 24);
        var window = Window();
        var store = new FakeStore(new FakeStoreViewBuilder()
            .WithOverrides([new DateOverride(date, [window], null) { Events = [] }])
            .Build());

        await ExecuteAsync(store, date, window.Id);

        var written = Assert.Single(store.Read().Overrides);
        Assert.NotNull(written.Events);
        Assert.Empty(written.Events);
    }

    private static AvailabilityWindow Window() => new(
        new WindowId("w_afternoon"),
        "Afternoon",
        new TimeOnly(13, 0),
        new TimeOnly(17, 0),
        TagSet.Empty);

    private static async Task ExecuteAsync(FakeStore store, DateOnly date, WindowId windowId)
    {
        var boundary = new DayBoundary(TimeZoneInfo.FindSystemTimeZoneById(DayBoundary.ZoneId));
        var matchingOn = new MatchingOn(
            store,
            new FixedTimeProvider(boundary.StartOf(date).AddHours(12)),
            boundary,
            new DimensionRegistry([]));

        await matchingOn.ExecuteAsync(
            new MatchingOnRequest(date, windowId, new Dictionary<DimensionId, IReadOnlyList<TagValue>>()),
            CancellationToken.None);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
