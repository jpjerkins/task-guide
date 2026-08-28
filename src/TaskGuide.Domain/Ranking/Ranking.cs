using TaskGuide.Domain.Tasks;

namespace TaskGuide.Domain.Ranking;

/// <summary>
/// Four keys, in order. The sort is <b>total</b> — every step is derived from data already on
/// screen, so any Task's position is explainable in a sentence. There is deliberately no
/// priority/importance field: self-assigned priorities rot.
/// </summary>
public sealed record RankKey(
    UrgencyBand Band,
    int Opportunities,
    int DurationRankDescending,
    DateTimeOffset CreatedAt) : IComparable<RankKey>
{
    /// <summary>
    /// Four keys, each read in its own natural ascending direction — the inversions the rules
    /// call for are already baked into the fields, so nothing here negates anything. Band 1
    /// (Deadline passed) is numerically lowest and therefore first; fewest Opportunities is
    /// <em>spend the rarest opportunity</em>; <see cref="DurationRankDescending"/> is named for
    /// the direction it encodes, so ascending on it puts the longest Duration first; oldest
    /// <see cref="CreatedAt"/> is the backstop.
    /// <para>
    /// Deliberately no weighted score of band and Scarcity (ADR-0004): the weights would have no
    /// principled value and <em>"why did this rank first"</em> would become unanswerable.
    /// </para>
    /// </summary>
    public int CompareTo(RankKey? other)
    {
        if (other is null) return -1;

        if (Band != other.Band) return Band.CompareTo(other.Band);
        if (Opportunities != other.Opportunities) return Opportunities.CompareTo(other.Opportunities);
        if (DurationRankDescending != other.DurationRankDescending)
        {
            return DurationRankDescending.CompareTo(other.DurationRankDescending);
        }

        return CreatedAt.CompareTo(other.CreatedAt);
    }
}

/// <summary>
/// Deadline enters the sort <b>bucketed, not continuous</b> — and the bands invent no
/// thresholds: they are exactly the three cases the Opportunity horizon rule already
/// distinguishes, so there is nothing to tune and nothing that can fall out of sync with it.
/// </summary>
public enum UrgencyBand
{
    DeadlinePassed = 1,

    /// <summary>Within the horizon — i.e. it clipped the rolling 7 days.</summary>
    WithinHorizon = 2,

    /// <summary>No Deadline, or one beyond the horizon.</summary>
    NoPressure = 3,
}

public static class Ranker
{
    /// <summary>
    /// Fewest Opportunities first — <em>spend the rarest opportunity</em>. Then longest Duration
    /// first ("the biggest Task that fits leads"), then oldest CreatedAt as a backstop reached
    /// only on an exact three-way tie.
    /// </summary>
    public static IReadOnlyList<TaskItem> Rank(
        IReadOnlyList<(TaskItem Task, RankKey Key)> eligible) =>
        eligible.OrderBy(entry => entry.Key).Select(entry => entry.Task).ToList();
}
