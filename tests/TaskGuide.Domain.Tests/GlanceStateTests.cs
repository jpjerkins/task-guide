using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Notifications;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.Domain.Time;
using Xunit;

namespace TaskGuide.Domain.Tests;

/// <summary>
/// #69/#76/#115/ADR-0011: a positional record's synthesised equality compares an
/// <c>IReadOnlyList</c> member by reference, so two structurally identical <see cref="GlanceState"/>s
/// built on different list instances would compare unequal — the same watch-budget bug ADR-0011
/// warns about (the Glance floor suppresses a redundant send by comparing states; a false
/// "changed" every tick burns watchOS's 50-updates-a-day budget in about 25 minutes). Compared with
/// <c>.Equals</c>, never <c>==</c> — <c>GlanceShape</c> is a <c>OneOfBase</c> subclass (a class),
/// so <c>==</c> is reference equality (ADR-0011).
/// <para>
/// The fixtures below build a fresh <see cref="TagSet"/> instance per call rather than sharing
/// <see cref="TagSet.Empty"/>, and cover Tag-bearing sets, not just empty ones: reusing the one
/// static <c>Empty</c> instance everywhere let this suite pass even while <c>TagSet</c> itself
/// still compared by reference (#115) — two separately-constructed, structurally-empty
/// <c>TagSet</c>s flip the pre-fix comparison to <c>False</c> where the shared instance would not.
/// </para>
/// </summary>
public sealed class GlanceStateTests
{
    private static readonly DimensionId Effort = new("effort");
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>A freshly-constructed, tag-free <see cref="TagSet"/> — never the shared <see cref="TagSet.Empty"/> instance.</summary>
    private static TagSet FreshEmptyTags() =>
        new(new Dictionary<DimensionId, IReadOnlyList<TagValue>>(), Array.Empty<LooseTag>());

    /// <summary>A freshly-constructed <see cref="TagSet"/> carrying one Dimension value, for exercising the Tag-bearing case.</summary>
    private static TagSet FreshTagBearingTags() =>
        new(
            new Dictionary<DimensionId, IReadOnlyList<TagValue>> { [Effort] = new[] { new TagValue("low") } },
            Array.Empty<LooseTag>());

    private static ResolvedWindow FreshWindow(TagSet tags) => new(
        new AvailabilityWindow(new WindowId("w_evening"), "Evening", new TimeOnly(18, 0), new TimeOnly(21, 0), tags),
        new DateTimeOffset(2026, 9, 3, 18, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 9, 3, 21, 0, 0, TimeSpan.Zero));

    private static TaskItem Task(string id, string title, TagSet tags) => new(
        new TaskId(id), title, null, tags, null, null, null, null, CreatedAt);

    [Fact]
    public void Two_structurally_equal_states_built_on_distinct_list_instances_compare_equal()
    {
        var shortlistA = new List<TaskItem> { Task("t_01ARZ3NDEKTSV4RRFFQ69G5001", "Fix the shelf bracket", FreshTagBearingTags()) };
        var shortlistB = new List<TaskItem> { Task("t_01ARZ3NDEKTSV4RRFFQ69G5001", "Fix the shelf bracket", FreshTagBearingTags()) };

        var a = new GlanceState(3, new InsideWindow(FreshWindow(FreshEmptyTags()), shortlistA, MatchingNow: 2));
        var b = new GlanceState(3, new InsideWindow(FreshWindow(FreshEmptyTags()), shortlistB, MatchingNow: 2));

        Assert.NotSame(shortlistA, shortlistB);
        Assert.True(a.Shape.Equals(b.Shape));
        Assert.True(a.Equals(b));
    }

    [Fact]
    public void Two_structurally_equal_states_with_tag_bearing_windows_compare_equal()
    {
        var shortlistA = new List<TaskItem> { Task("t_01ARZ3NDEKTSV4RRFFQ69G5001", "Fix the shelf bracket", FreshEmptyTags()) };
        var shortlistB = new List<TaskItem> { Task("t_01ARZ3NDEKTSV4RRFFQ69G5001", "Fix the shelf bracket", FreshEmptyTags()) };

        var a = new GlanceState(3, new InsideWindow(FreshWindow(FreshTagBearingTags()), shortlistA, MatchingNow: 2));
        var b = new GlanceState(3, new InsideWindow(FreshWindow(FreshTagBearingTags()), shortlistB, MatchingNow: 2));

        Assert.True(a.Shape.Equals(b.Shape));
        Assert.True(a.Equals(b));
    }

    [Fact]
    public void Two_structurally_equal_next_window_states_built_on_distinct_list_instances_compare_equal()
    {
        var shortlistA = new List<TaskItem> { Task("t_01ARZ3NDEKTSV4RRFFQ69G5001", "Fix the shelf bracket", FreshTagBearingTags()) };
        var shortlistB = new List<TaskItem> { Task("t_01ARZ3NDEKTSV4RRFFQ69G5001", "Fix the shelf bracket", FreshTagBearingTags()) };

        var a = new GlanceState(3, new NextWindow(FreshWindow(FreshEmptyTags()), shortlistA));
        var b = new GlanceState(3, new NextWindow(FreshWindow(FreshEmptyTags()), shortlistB));

        Assert.NotSame(shortlistA, shortlistB);
        Assert.True(a.Shape.Equals(b.Shape));
        Assert.True(a.Equals(b));
    }

    [Fact]
    public void Two_structurally_equal_next_window_states_with_tag_bearing_windows_compare_equal()
    {
        var shortlistA = new List<TaskItem> { Task("t_01ARZ3NDEKTSV4RRFFQ69G5001", "Fix the shelf bracket", FreshEmptyTags()) };
        var shortlistB = new List<TaskItem> { Task("t_01ARZ3NDEKTSV4RRFFQ69G5001", "Fix the shelf bracket", FreshEmptyTags()) };

        var a = new GlanceState(3, new NextWindow(FreshWindow(FreshTagBearingTags()), shortlistA));
        var b = new GlanceState(3, new NextWindow(FreshWindow(FreshTagBearingTags()), shortlistB));

        Assert.True(a.Shape.Equals(b.Shape));
        Assert.True(a.Equals(b));
    }

    [Fact]
    public void A_genuine_difference_in_matching_now_still_compares_unequal()
    {
        var shortlist = new List<TaskItem> { Task("t_01ARZ3NDEKTSV4RRFFQ69G5001", "Fix the shelf bracket", FreshEmptyTags()) };

        var a = new GlanceState(3, new InsideWindow(FreshWindow(FreshEmptyTags()), shortlist, MatchingNow: 2));
        var b = new GlanceState(3, new InsideWindow(FreshWindow(FreshEmptyTags()), shortlist, MatchingNow: 5));

        Assert.False(a.Shape.Equals(b.Shape));
    }

    [Fact]
    public void A_genuine_difference_in_the_shortlist_still_compares_unequal()
    {
        var shortlistA = new List<TaskItem> { Task("t_01ARZ3NDEKTSV4RRFFQ69G5001", "Fix the shelf bracket", FreshEmptyTags()) };
        var shortlistB = new List<TaskItem> { Task("t_01ARZ3NDEKTSV4RRFFQ69G5002", "Water the plants", FreshEmptyTags()) };

        var a = new GlanceState(3, new InsideWindow(FreshWindow(FreshEmptyTags()), shortlistA, MatchingNow: 2));
        var b = new GlanceState(3, new InsideWindow(FreshWindow(FreshEmptyTags()), shortlistB, MatchingNow: 2));

        Assert.False(a.Shape.Equals(b.Shape));
    }
}
