using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;
using TaskGuide.Infrastructure.Storage;
using TaskGuide.TestSupport;
using Xunit;

namespace TaskGuide.Infrastructure.Tests;

/// <summary>
/// `tests/TEST-INVENTORY.md`, "Test support (#77)": the real end-to-end check for review finding
/// 1, run against the actual <see cref="DayShapeReader"/> rather than re-walking its three steps
/// by hand — <see cref="TaskGuide.Application.Tests.FakeStoreViewTests"/> carries that proxy
/// version because it cannot reference <c>TaskGuide.Infrastructure</c>.
/// </summary>
public sealed class DayShapeReaderFakeStoreTests
{
    [Fact]
    public void An_unseeded_FakeStore_handed_to_DayShapeReader_returns_a_usable_DayShape()
    {
        var store = new FakeStore();
        var reader = new DayShapeReader(store);
        var date = new DateOnly(2026, 9, 5);

        var shape = reader.For(date);

        Assert.Equal(date, shape.Date);
        Assert.False(shape.IsOverridden);
        Assert.Empty(shape.Windows);
        Assert.Empty(shape.Events);
    }

    /// <summary>#116 finding 1: a `DayTemplatesWrite` through `MutateAsync` re-points the
    /// builder's default Pattern at the first seeded template, so a store seeded with templates
    /// alone — via a real write, not just `WithDayTemplates` — still resolves end to end.</summary>
    [Fact]
    public async Task A_DayTemplatesWrite_through_MutateAsync_re_points_the_builders_default_Pattern_the_same_way()
    {
        var mine = new DayTemplate(new DayTemplateId("dt_mine"), "My day", [], []);
        var store = new FakeStore();
        await store.MutateAsync<Never>(
            _ => new StoreMutation([new DayTemplatesWrite([mine])]), CancellationToken.None);
        var reader = new DayShapeReader(store);
        var date = new DateOnly(2026, 9, 4);

        var shape = reader.For(date);

        Assert.Equal(date, shape.Date);
        Assert.False(shape.IsOverridden);
    }

    /// <summary>#116 finding 1: the re-pointing above only holds while the builder's synthetic
    /// default pair is still intact — an unrelated write in between must not disturb it, or the
    /// very next `DayTemplatesWrite` would stop re-pointing as soon as a test did anything else
    /// first.</summary>
    [Fact]
    public async Task The_builders_default_pair_survives_an_unrelated_write_so_a_later_DayTemplatesWrite_still_re_points()
    {
        var mine = new DayTemplate(new DayTemplateId("dt_mine"), "My day", [], []);
        var store = new FakeStore();
        var reader = new DayShapeReader(store);
        var date = new DateOnly(2026, 9, 4);

        // Two unrelated writes before the real seed — repeated builds off the still-intact
        // default pair must not accidentally mark it caller-supplied (idempotence of
        // DefaultPairIntact while it stays true).
        await store.MutateAsync<Never>(_ => new StoreMutation([new TasksWrite([])]), CancellationToken.None);
        await store.MutateAsync<Never>(_ => new StoreMutation([new TasksWrite([])]), CancellationToken.None);

        await store.MutateAsync<Never>(
            _ => new StoreMutation([new DayTemplatesWrite([mine])]), CancellationToken.None);

        var shape = reader.For(date);
        Assert.Equal(date, shape.Date);
        Assert.False(shape.IsOverridden);
    }

    /// <summary>#116 finding 1: once real Day templates have been seeded, a later
    /// `DayTemplatesWrite` must leave the derived Pattern book alone rather than silently
    /// re-pointing it — an orphaned template must surface exactly as it would in production,
    /// matching `JsonStore`, which does no fix-up.</summary>
    [Fact]
    public async Task Once_real_Day_templates_have_been_seeded_a_DayTemplatesWrite_leaves_the_derived_Pattern_book_alone()
    {
        var a = new DayTemplate(new DayTemplateId("dt_a"), "A", [], []);
        var b = new DayTemplate(new DayTemplateId("dt_b"), "B", [], []);
        var store = new FakeStore(new FakeStoreViewBuilder().WithDayTemplates([a, b]).Build());
        var reader = new DayShapeReader(store);
        var date = new DateOnly(2026, 9, 4);

        // Two unrelated writes off the already non-intact view — repeated builds must not
        // resurrect the fix-up once it's gone (idempotence of DefaultPairIntact while it stays
        // false).
        await store.MutateAsync<Never>(_ => new StoreMutation([new TasksWrite([])]), CancellationToken.None);
        await store.MutateAsync<Never>(_ => new StoreMutation([new TasksWrite([])]), CancellationToken.None);

        await store.MutateAsync<Never>(
            _ => new StoreMutation([new DayTemplatesWrite([b])]), CancellationToken.None);

        var exception = Assert.Throws<InvalidOperationException>(() => reader.For(date));
        Assert.Contains(a.Id.Value, exception.Message);
    }
}
