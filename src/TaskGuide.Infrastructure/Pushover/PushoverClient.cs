using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Notifications;

namespace TaskGuide.Infrastructure.Pushover;

/// <summary>
/// <see cref="IPushoverClient"/> for the walking skeleton (#51/#3): plain HTTPS form POST to
/// <c>https://api.pushover.net/1/messages.json</c> (docs/research/pushover-api.md), the two
/// static secrets read from configuration, priority always 0 or lower — <b>never 1</b>, which
/// bypasses the user's own quiet hours and nothing this system sends is worth that override.
/// </summary>
/// <remarks>
/// A message is a doorbell carrying at most one <c>url</c>. The walking skeleton only needs a
/// Receipt to prove the integration end to end; <see cref="SendReminderAsync"/> and
/// <see cref="SendGlanceAsync"/> map their record's fields straight across without the ranking,
/// shortlist or footer formatting a later ticket owns — that logic doesn't exist yet, so it
/// isn't faked here.
/// </remarks>
public sealed class PushoverClient(HttpClient httpClient, IOptions<PushoverOptions> options, ILogger<PushoverClient> logger) : IPushoverClient
{
    private const string MessagesUrl = "https://api.pushover.net/1/messages.json";

    public async Task<bool> SendReminderAsync(Reminder reminder, CancellationToken cancellationToken) =>
        await SendAsync(reminder.Title, reminder.WindowContext, reminder.LandingPage, priority: 0, cancellationToken);

    /// <summary>Fire-and-forget (#51's own contract on <see cref="IPushoverClient"/>): one attempt, failure logged, never retried.</summary>
    public async Task SendReceiptAsync(Receipt receipt, CancellationToken cancellationToken)
    {
        try
        {
            await SendAsync(Receipt.FixedTitle, receipt.TaskTitle, receipt.DetailPage, priority: 0, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Pushover Receipt send failed for Task {TaskId}; not retried", receipt.TaskId);
        }
    }

    public async Task<bool> SendGlanceAsync(Glance glance, CancellationToken cancellationToken)
    {
        var pushoverOptions = options.Value;
        if (!pushoverOptions.IsConfigured)
        {
            logger.LogWarning("Pushover is not configured (missing Token or UserKey); skipping Glance send");
            return false;
        }

        try
        {
            using var response = await httpClient.PostAsync(
                "https://api.pushover.net/1/glances.json",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["token"] = pushoverOptions.Token!,
                    ["user"] = pushoverOptions.UserKey!,
                    ["title"] = glance.Line1,
                    ["subtext"] = glance.Line2,
                    ["text"] = glance.Line3,
                    ["count"] = glance.Count.ToString(),
                }),
                cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Pushover Glance send failed");
            return false;
        }
    }

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
