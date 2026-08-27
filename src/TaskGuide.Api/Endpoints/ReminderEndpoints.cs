namespace TaskGuide.Api.Endpoints;

/// <summary>
/// The landing page a notification opens — the highest-traffic screen in the system, because
/// Pushover carries one URL and nothing actionable, so snooze and inline triage have nowhere
/// else to live.
/// </summary>
public static class ReminderEndpoints
{
    public static RouteGroupBuilder MapReminderEndpoints(this RouteGroupBuilder api)
    {
        var reminders = api.MapGroup("/reminders").WithTags("Reminders");

        // Keyed like the Fire record: (date, windowId, kind). Shows ALL matches, not the push's
        // three. The response carries the page-level gate — is this page still about a live day —
        // which is what disables Snooze and "Matching on" while Mark off and Postpone stay live.
        reminders.MapGet("/{date}/{windowId}", (string date, string windowId) => Results.NoContent());

        // The predicate is server-side and the UI reads it. A crossing request is REJECTED, and
        // the rejection renders as the same line the disabled state would have shown.
        reminders.MapPost("/{date}/{windowId}/snooze", (string date, string windowId) => Results.NoContent());

        // The audit trail for "why did I not get a reminder?" — 30-day retention.
        reminders.MapGet("/fires/{date}", (string date) => Results.NoContent());

        return reminders;
    }
}
