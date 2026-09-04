using OneOf;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Tasks;
using TaskGuide.Domain.Time;

namespace TaskGuide.Domain.Notifications;

/// <summary>
/// A silent readout of current state on a watch complication — never a claim, never a doorbell.
/// <b>A Glance is not a Notification</b>: no URL, no sound, no delivery promise, nothing reaching
/// Notification Center. The silence guarantee is therefore untouched, Liveness does not read it,
/// and <b>nothing in this system may depend on it</b>.
/// </summary>
/// <param name="Count">
/// The to-process backlog — the only field that renders unlabelled in a small slot, so it must
/// mean something with no words attached, and it is deliberately window-independent.
/// </param>
public sealed record GlanceState(int Count, GlanceShape Shape);

/// <summary>
/// The discriminated inside-a-Window / next-Window shape, with no line slots and no character
/// budget — rendering into a complication's three ~20-character lines is Infrastructure's job.
/// </summary>
[GenerateOneOf]
public partial class GlanceShape : OneOfBase<InsideWindow, NextWindow>;

/// <param name="MatchingNow">
/// The total the "+N more doable now" line needs — it appears nowhere else on the face.
/// </param>
public sealed record InsideWindow(ResolvedWindow Window, IReadOnlyList<TaskItem> Shortlist, int MatchingNow)
{
    /// <summary>
    /// A positional record's synthesised equality compares <see cref="Shortlist"/> by
    /// <b>reference</b>, so two structurally identical states built on different list instances
    /// would compare unequal — the same watch-budget bug ADR-0011 warns about (#69: the Glance
    /// floor suppresses a redundant send by comparing states), arriving through a different door.
    /// <see cref="StructuralEquality"/>'s sequence equality closes it.
    /// </summary>
    public bool Equals(InsideWindow? other) =>
        other is not null
        && Window == other.Window
        && MatchingNow == other.MatchingNow
        && StructuralEquality.SequenceEqual(Shortlist, other.Shortlist);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Window);
        hash.Add(MatchingNow);
        hash.Add(StructuralEquality.SequenceHash(Shortlist));
        return hash.ToHashCode();
    }
}

public sealed record NextWindow(ResolvedWindow Window, IReadOnlyList<TaskItem> Shortlist)
{
    /// <summary>See <see cref="InsideWindow.Equals(InsideWindow?)"/> — same trap, same fix.</summary>
    public bool Equals(NextWindow? other) =>
        other is not null
        && Window == other.Window
        && StructuralEquality.SequenceEqual(Shortlist, other.Shortlist);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Window);
        hash.Add(StructuralEquality.SequenceHash(Shortlist));
        return hash.ToHashCode();
    }
}
