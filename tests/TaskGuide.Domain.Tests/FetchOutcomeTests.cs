using TaskGuide.Domain.Common;
using Xunit;

namespace TaskGuide.Domain.Tests;

/// <summary>
/// #69/#76: a fetched Dimension source returns <see cref="FetchOutcome{T}"/>, not a nullable list —
/// <c>Known</c> and <c>Unavailable</c> must each <c>Match</c> to their own arm.
/// </summary>
public sealed class FetchOutcomeTests
{
    [Fact]
    public void Known_matches_to_the_known_arm_and_yields_its_value()
    {
        FetchOutcome<int> outcome = new Known<int>(42);

        var matched = outcome.Match(
            known => known.Value,
            unavailable => -1);

        Assert.Equal(42, matched);
    }

    [Fact]
    public void Unavailable_matches_to_the_unavailable_arm_and_yields_its_reason()
    {
        FetchOutcome<int> outcome = new Unavailable("timed out");

        var matched = outcome.Match(
            known => "should not reach here",
            unavailable => unavailable.Reason);

        Assert.Equal("timed out", matched);
    }
}
