using TaskGuide.Domain.Common;
using TaskGuide.Domain.Notifications;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using TaskGuide.Domain.Time;
using Xunit;

namespace TaskGuide.Domain.Tests;

/// <summary>
/// #69/#76/ADR-0011: a positional record's synthesised equality compares an
/// <c>IReadOnlyList</c> member by reference, so two structurally identical <see cref="GlanceState"/>s
/// built on different list instances would compare unequal — the same watch-budget bug ADR-0011
/// warns about (the Glance floor suppresses a redundant send by comparing states; a false
/// "changed" every tick burns watchOS's 50-updates-a-day budget in about 25 minutes). Compared with
/// <c>.Equals</c>, never <c>==</c> — <c>GlanceShape</c> is a <c>OneOfBase</c> subclass (a class),
/// so <c>==</c> is reference equality (ADR-0011).
/// </summary>
public sealed class GlanceStateTests
{
    private static readonly ResolvedWindow Window = new(
        new AvailabilityWindow(new WindowId("w_evening"), "Evening", new TimeOnly(18, 0), new TimeOnly(21, 0), TagSet.Empty),
        new DateTimeOffset(2026, 9, 3, 18, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 9, 3, 21, 0, 0, TimeSpan.Zero));

    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    private static TaskItem Task(string id, string title) => new(
        new TaskId(id), title, null, TagSet.Empty, null, null, null, null, CreatedAt);

    [Fact]
    public void Two_structurally_equal_states_built_on_distinct_list_instances_compare_equal()
    {
        var shortlistA = new List<TaskItem> { Task("t_01ARZ3NDEKTSV4RRFFQ69G5001", "Fix the shelf bracket") };
        var shortlistB = new List<TaskItem> { Task("t_01ARZ3NDEKTSV4RRFFQ69G5001", "Fix the shelf bracket") };

        var a = new GlanceState(3, new InsideWindow(Window, shortlistA, MatchingNow: 2));
        var b = new GlanceState(3, new InsideWindow(Window, shortlistB, MatchingNow: 2));

        Assert.NotSame(shortlistA, shortlistB);
        Assert.True(a.Shape.Equals(b.Shape));
        Assert.True(a.Equals(b));
    }

    [Fact]
    public void A_genuine_difference_in_matching_now_still_compares_unequal()
    {
        var shortlist = new List<TaskItem> { Task("t_01ARZ3NDEKTSV4RRFFQ69G5001", "Fix the shelf bracket") };

        var a = new GlanceState(3, new InsideWindow(Window, shortlist, MatchingNow: 2));
        var b = new GlanceState(3, new InsideWindow(Window, shortlist, MatchingNow: 5));

        Assert.False(a.Shape.Equals(b.Shape));
    }

    [Fact]
    public void A_genuine_difference_in_the_shortlist_still_compares_unequal()
    {
        var shortlistA = new List<TaskItem> { Task("t_01ARZ3NDEKTSV4RRFFQ69G5001", "Fix the shelf bracket") };
        var shortlistB = new List<TaskItem> { Task("t_01ARZ3NDEKTSV4RRFFQ69G5002", "Water the plants") };

        var a = new GlanceState(3, new InsideWindow(Window, shortlistA, MatchingNow: 2));
        var b = new GlanceState(3, new InsideWindow(Window, shortlistB, MatchingNow: 2));

        Assert.False(a.Shape.Equals(b.Shape));
    }
}
