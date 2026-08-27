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
/// deliberately not the Day boundary: a Receipt is tied to an act, not to the calendar.
/// <b>Fire-and-forget</b> — one attempt, failure logged, never retried; a failed Receipt is
/// indistinguishable from an ignored one, and a retry would re-notify for something quite
/// possibly already dismissed. Not in the Fire record: application log only.
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
public enum CaptureSource
{
    /// <summary>In-app capture already provides confirmation and Tag entry structurally.</summary>
    App,

    QuickTaskShortcut,
    DetailedTaskShortcut,
    SmartAddTaskShortcut,
    Api,
}
