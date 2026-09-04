using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Tags;
using TaskGuide.Domain.Tasks;
using Xunit;

namespace TaskGuide.Domain.Tests;

/// <summary>
/// #114 sweeps the #115 trap across the Dimensions and Tasks records. A categorical axis'
/// <see cref="CategoricalDimension.DeclaredValues"/> and the registry's
/// <see cref="DimensionRegistry.Dimensions"/> are multisets; an ordinal axis'
/// <see cref="OrdinalDimension.OrderedValues"/> is a sequence, since <c>RankOf</c> returns the
/// index and a reorder is a different axis. <see cref="CompletionLog.Entries"/> is a multiset —
/// <c>Latest</c> is a <c>MaxBy</c> and <c>Covers</c> an <c>Any</c>, neither reads a position.
/// <see cref="EveryNWeeksOn.Weekdays"/> is a multiset, compared through
/// <see cref="Recurrence.Rule"/> with <c>.Equals</c> — never <c>==</c>, which is reference
/// equality on a closed-set hierarchy.
/// </summary>
public sealed class DimensionAndTaskRecordEqualityTests
{
    private static readonly DimensionId Effort = new("effort");
    private static readonly TagValue Low = new("low");
    private static readonly TagValue Medium = new("medium");
    private static readonly TagValue High = new("high");

    [Fact]
    public void CategoricalDimension_DeclaredValues_compares_equal_regardless_of_order()
    {
        var a = new CategoricalDimension(Effort, "Effort", new[] { Low, Medium });
        var b = new CategoricalDimension(Effort, "Effort", new[] { Medium, Low });

        Assert.NotSame(a.DeclaredValues, b.DeclaredValues);
        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void OrdinalDimension_OrderedValues_compares_unequal_when_reordered()
    {
        var a = new OrdinalDimension(Effort, "Effort", new[] { Low, Medium, High }, Low, null);
        var b = new OrdinalDimension(Effort, "Effort", new[] { Medium, Low, High }, Low, null);

        Assert.NotSame(a.OrderedValues, b.OrderedValues);
        Assert.False(a.Equals(b));
    }

    [Fact]
    public void Two_separately_constructed_structurally_identical_OrdinalDimensions_compare_equal()
    {
        var a = new OrdinalDimension(Effort, "Effort", new[] { Low, Medium, High }, Low, null);
        var b = new OrdinalDimension(Effort, "Effort", new[] { Low, Medium, High }, Low, null);

        Assert.NotSame(a.OrderedValues, b.OrderedValues);
        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void An_OrdinalDimension_differing_only_in_TaskDefault_or_WindowDefault_compares_unequal()
    {
        var baseline = new OrdinalDimension(Effort, "Effort", new[] { Low, Medium }, Low, Medium);
        var differentTaskDefault = new OrdinalDimension(Effort, "Effort", new[] { Low, Medium }, Medium, Medium);
        var differentWindowDefault = new OrdinalDimension(Effort, "Effort", new[] { Low, Medium }, Low, Low);

        Assert.False(baseline.Equals(differentTaskDefault));
        Assert.False(baseline.Equals(differentWindowDefault));
    }

    [Fact]
    public void DimensionRegistry_Dimensions_compares_equal_regardless_of_order()
    {
        var categorical = new CategoricalDimension(Effort, "Effort", new[] { Low, Medium });
        var location = new DimensionId("location");
        var ordinal = new OrdinalDimension(location, "Location", new[] { Low, High }, null, null);

        var a = new DimensionRegistry(new Dimension[] { categorical, ordinal });
        var b = new DimensionRegistry(new Dimension[] { ordinal, categorical });

        Assert.NotSame(a.Dimensions, b.Dimensions);
        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void A_CategoricalDimension_and_an_OrdinalDimension_with_the_same_id_label_and_values_compare_unequal()
    {
        var categorical = new CategoricalDimension(Effort, "Effort", new[] { Low, Medium });
        var ordinal = new OrdinalDimension(Effort, "Effort", new[] { Low, Medium }, null, null);

        Assert.False(categorical.Equals(ordinal));
    }

    [Fact]
    public void CompletionLog_Entries_compares_equal_regardless_of_order()
    {
        var taskId = new TaskId("t_1");
        var e1 = new CompletionEntry(new DateOnly(2026, 1, 1), new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));
        var e2 = new CompletionEntry(new DateOnly(2026, 1, 2), new DateTimeOffset(2026, 1, 2, 9, 0, 0, TimeSpan.Zero));

        var a = new CompletionLog(taskId, new[] { e1, e2 });
        var b = new CompletionLog(taskId, new[] { e2, e1 });

        Assert.NotSame(a.Entries, b.Entries);
        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void A_CompletionLog_holding_one_entry_twice_compares_unequal_to_one_holding_it_once()
    {
        var taskId = new TaskId("t_1");
        var entry = new CompletionEntry(new DateOnly(2026, 1, 1), new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));

        var a = new CompletionLog(taskId, new[] { entry, entry });
        var b = new CompletionLog(taskId, new[] { entry });

        Assert.False(a.Equals(b));
    }

    [Fact]
    public void EveryNWeeksOn_Weekdays_compares_equal_regardless_of_order()
    {
        var a = new Recurrence(
            RecurrenceAnchor.Calendar,
            new EveryNWeeksOn(1, new[] { DayOfWeek.Monday, DayOfWeek.Wednesday }),
            null);
        var b = new Recurrence(
            RecurrenceAnchor.Calendar,
            new EveryNWeeksOn(1, new[] { DayOfWeek.Wednesday, DayOfWeek.Monday }),
            null);

        Assert.NotSame(((EveryNWeeksOn)a.Rule).Weekdays, ((EveryNWeeksOn)b.Rule).Weekdays);
        Assert.True(a.Rule.Equals(b.Rule));
        Assert.Equal(a.Rule.GetHashCode(), b.Rule.GetHashCode());
    }
}
