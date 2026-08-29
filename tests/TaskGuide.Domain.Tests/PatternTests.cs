using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;
using Xunit;

namespace TaskGuide.Domain.Tests;

/// <summary>
/// ADR-0010(b): <see cref="PatternBook.Active"/> is a dangling-reference read — it throws, and the
/// throw names both ends.
/// </summary>
public sealed class PatternTests
{
    private static Pattern PatternOf(string id) =>
        new(new PatternId(id), "Pattern", [.. Enumerable.Repeat(new DayTemplateId("dt_1"), 7)]);

    [Fact]
    public void An_active_Pattern_id_matching_no_Pattern_throws_naming_the_active_id()
    {
        var book = new PatternBook(new PatternId("p_missing"), [PatternOf("p_other")]);

        var ex = Assert.Throws<InvalidOperationException>(() => book.Active);

        Assert.Contains("p_missing", ex.Message);
    }
}
