using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Notifications;

namespace TaskGuide.Infrastructure.Pushover;

/// <summary>
/// <see cref="IReminderSender"/>, <see cref="IReceiptSender"/> and <see cref="IGlanceSender"/> for
/// the walking skeleton (#51/#3): plain HTTPS form POST to
/// <c>https://api.pushover.net/1/messages.json</c> (docs/research/pushover-api.md), the two
/// static secrets read from configuration, priority always 0 or lower — <b>never 1</b>, which
/// bypasses the user's own quiet hours and nothing this system sends is worth that override.
/// The vendor name — Pushover is the one notification vendor this system uses — stays confined to
/// Infrastructure; the three ports it implements name no vendor.
/// </summary>
/// <remarks>
/// A message is a doorbell carrying at most one <c>url</c>. The walking skeleton only needs a
/// Receipt to prove the integration end to end; <see cref="SendReminderAsync"/> maps its record's
/// fields straight across without the ranking, shortlist or footer formatting a later ticket
/// owns — that logic doesn't exist yet, so it isn't faked here.
/// </remarks>
public sealed class PushoverClient(IHttpClientFactory httpClientFactory, IOptions<PushoverOptions> options, ILogger<PushoverClient> logger)
    : IReminderSender, IReceiptSender, IGlanceSender
{
    private const string MessagesUrl = "https://api.pushover.net/1/messages.json";

    // Named client resolved via IHttpClientFactory, not stored in a field: this client is a
    // singleton living for the process lifetime (#76), and holding one HttpClient — and so one
    // HttpMessageHandler — that long would defeat the factory's handler rotation, which exists so
    // a long-lived process picks up DNS changes. Pushover sits behind a CDN whose IPs move, and
    // this process (the pi5's TickLoop) runs for months. The failure mode is silent — IHealth
    // deliberately excludes Pushover reachability from liveness, so pushes would just stop with
    // /health still green — which is exactly the kind of thing that gets "optimised" back into a
    // field by someone who doesn't know why it's here. Don't.
    public const string HttpClientName = "pushover";

    public async Task<bool> SendReminderAsync(Reminder reminder, CancellationToken cancellationToken) =>
        await SendAsync(reminder.Title, reminder.WindowContext, reminder.LandingPage, priority: 0, cancellationToken);

    /// <summary>
    /// Adapters never throw for expected failures; they return an outcome (#69) — "logged, never
    /// retried" beyond what <see cref="SendAsync"/> already attempts is the caller's policy now,
    /// not something this adapter hides.
    /// </summary>
    public async Task<bool> SendReceiptAsync(Receipt receipt, CancellationToken cancellationToken)
    {
        try
        {
            return await SendAsync(Receipt.FixedTitle, receipt.TaskTitle, receipt.DetailPage, priority: 0, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Pushover Receipt send failed for Task {TaskId}; not retried", receipt.TaskId);
            return false;
        }
    }

    // Rendering a GlanceState into a complication's three text slots is the Adapters lane's
    // renderer, which does not exist yet (#76 does not invent one). Unreachable in production
    // today — nothing calls SendGlanceAsync, and nothing in the tree implements IGlanceSender any
    // more (TickLoopTests dropped its fake) — which is why throwing here, rather than preserving
    // behaviour, is acceptable for this ticket.
    //
    // The contract this throw stands in for, for whoever writes the renderer: POST
    // https://api.pushover.net/1/glances.json (docs/research/pushover-api.md), form fields
    // "title", "subtext", "text" and "count" — the four the Glance endpoint takes, alongside the
    // usual "token"/"user".
    public Task<bool> SendGlanceAsync(GlanceState state, CancellationToken cancellationToken) =>
        throw new NotImplementedException("Rendering a GlanceState into Pushover's Glance fields belongs to the Adapters lane.");

    private async Task<bool> SendAsync(string title, string message, Uri url, int priority, CancellationToken cancellationToken)
    {
        var pushoverOptions = options.Value;
        if (!pushoverOptions.IsConfigured)
        {
            logger.LogWarning("Pushover is not configured (missing Token or UserKey); skipping send");
            return false;
        }

        try
        {
            var httpClient = httpClientFactory.CreateClient(HttpClientName);
            using var response = await httpClient.PostAsync(
                MessagesUrl,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["token"] = pushoverOptions.Token!,
                    ["user"] = pushoverOptions.UserKey!,
                    ["title"] = title,
                    ["message"] = message,
                    ["url"] = url.ToString(),
                    ["priority"] = priority.ToString(),
                }),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Pushover send failed with status {StatusCode}", response.StatusCode);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Pushover send failed");
            return false;
        }
    }
}
