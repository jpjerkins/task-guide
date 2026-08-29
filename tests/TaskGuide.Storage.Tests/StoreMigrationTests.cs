using TaskGuide.Infrastructure.Storage;
using Xunit;

namespace TaskGuide.Storage.Tests;

/// <summary>
/// Against `tests/TEST-INVENTORY.md`'s "a migration step must move the store version strictly
/// forward". Per ADR-0009 this is an invariant of the <em>step</em>, enforced where the step is
/// built — not a condition <see cref="StartupSequence"/>'s walk discovers at startup.
/// </summary>
public sealed class StoreMigrationTests
{
    private static Task NoOp(string dataDir, CancellationToken cancellationToken) => Task.CompletedTask;

    [Fact]
    public void A_step_that_moves_the_version_forward_is_accepted()
    {
        var step = new StoreMigration(1, 2, NoOp);

        Assert.Equal(1, step.From);
        Assert.Equal(2, step.To);
    }

    [Fact]
    public void A_step_that_leaves_the_version_where_it_found_it_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new StoreMigration(1, 1, NoOp));
    }

    [Fact]
    public void A_step_that_moves_the_version_backward_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new StoreMigration(2, 1, NoOp));
    }

    /// <summary>
    /// The invariant has to hold at exactly one door. A record's copy constructor is a second one —
    /// `step with { To = 1 }` reaches a walk without passing the constructor — so the type is
    /// deliberately not a record, and this pins that against a future "make it a record" tidy-up.
    /// </summary>
    [Fact]
    public void A_step_is_not_a_record_so_the_constructor_is_its_only_door()
    {
        var clone = typeof(StoreMigration).GetMethod(
            "<Clone>$",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.Null(clone);
    }
}
