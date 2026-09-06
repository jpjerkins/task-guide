using TaskGuide.Domain.Common;

namespace TaskGuide.Domain.Notifications;

/// <summary>
/// Confirms that a Task was captured, and is the doorway to everything the capture path could
/// not ask for. No capture path collects Tags, so without the Receipt they could only be added
/// by remembering to open the app later — the Receipt is what makes ruling Tags out of capture
/// safe.
/// </summary>
/// <remarks>
/// Priority always 0 — the buzz <em>is</em> the confirmation. `ttl` 24 hours <b>from sending</b>,
/// deliberately not the Day boundary: a Receipt is tied to an act, not to the calendar. Delivery
/// is attempted inline by capture; a failed Receipt does not make capture fail. Not in the Fire
/// record: application log only.
/// <para>
/// The fixed title "Added" resolves a collision — a Reminder's title is a Task title in full.
/// Putting the Task's Title verbatim in the body lets the Receipt double as parse verification
/// for Smart Add Task.
/// </para>
/// </remarks>
public sealed record Receipt(TaskId TaskId, string TaskTitle, string Duration, Uri DetailPage)
{
    public static string FixedTitle => "Added";
}

/// <summary>
/// Sent for every capture that happens <b>outside</b> the app, and for no capture made inside
/// it. The capture endpoint carries the source and this policy reads it, keeping the decision in
/// one place rather than letting each capture path decide.
/// </summary>
public static class ReceiptPolicy
{
    /// <summary>The one source that is already structurally confirmed by the open app.</summary>
    public const string InAppSource = "app";

    /// <summary>
    /// Sources are deliberately open. Only an in-app capture is un-Receipted; every other value,
    /// including a newly introduced or misspelt source, earns a Receipt.
    /// </summary>
    public static bool IsEarned(string source) => !string.Equals(source, InAppSource, StringComparison.Ordinal);
}
