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
public sealed class PushoverClient(
    IHttpClientFactory httpClientFactory,
    IOptions<PushoverOptions> options,
    ILogger<PushoverClient> logger,
    TimeProvider timeProvider)
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

    // "Up to three attempts", "roughly 3 seconds per attempt with a short backoff between" —
    // CONTEXT.md § Receipt (lines 1319-1367). All three constants are the *Receipt's*, and the
    // timeout is applied per Receipt attempt rather than on the named HttpClient, which all three
    // senders share: a 3s ceiling on SendReminderAsync would make Pushover-is-slow look like
    // Pushover-refused, leave firedAt unwritten, and re-fire the same Reminder next tick — a
    // duplicate push on a path that never asked for a budget.
    private const int MaxReceiptAttempts = 3;
    private static readonly TimeSpan ReceiptBackoff = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ReceiptAttemptTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// One attempt only — a failed push reads as still-unfired next tick, and it's the tick loop
    /// that retries it (<see cref="IReminderSender.SendReminderAsync"/>'s own doc comment).
    /// Retrying here too would double-notify.
    /// </summary>
    public async Task<bool> SendReminderAsync(Reminder reminder, CancellationToken cancellationToken)
    {
        if (Credentials() is not { } credentials)
        {
            return false;
        }

        // No per-attempt timeout: that budget is the Receipt's, and the tick loop is this
        // sender's retry. See the constants above.
        var attempt = await SendAsync(
            credentials, reminder.Title, reminder.WindowContext, reminder.LandingPage, priority: 0, cancellationToken, cancellationToken);
        return attempt.Match(accepted => true, refused => false, rejected => false);
    }

    /// <summary>
    /// Up to three attempts, only while Pushover has not accepted — <see cref="IReceiptSender"/>'s
    /// own doc comment carries the contract in full. Each attempt's outcome, and the swallow of
    /// whatever exception produced it, is <see cref="SendAsync"/>'s job (#69: adapters return an
    /// outcome, they don't throw for expected failures); this loop only ever sees the resulting
    /// <see cref="PushoverAttempt"/>. Both diagnostics naming the Task live here: one when a 4xx
    /// ends it early, one when all three attempts are spent. Every way a Receipt can fail for
    /// good is traceable back to its Task — the defect #118 was filed about.
    /// </summary>
    public async Task<bool> SendReceiptAsync(Receipt receipt, CancellationToken cancellationToken)
    {
        if (Credentials() is not { } credentials)
        {
            return false;
        }

        for (var attemptNumber = 1; attemptNumber <= MaxReceiptAttempts; attemptNumber++)
        {
            // Roughly 3s per attempt: a linked source, so the timeout cancels this attempt alone
            // while the caller's own token still cancels the whole send.
            using var timeout = new CancellationTokenSource(ReceiptAttemptTimeout, timeProvider);
            using var attemptTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            var attempt = await SendAsync(
                credentials, Receipt.FixedTitle, receipt.TaskTitle, receipt.DetailPage, priority: 0, attemptTokenSource.Token, cancellationToken);
            var outcome = attempt.Match(
                accepted => (bool?)true,
                refused => null, // not accepted yet — worth another attempt
                rejected =>
                {
                    logger.LogError(
                        "Pushover rejected the Receipt for Task {TaskId} with status {StatusCode}; not retried",
                        receipt.TaskId,
                        rejected.StatusCode);
                    return (bool?)false;
                });

            if (outcome is { } done)
            {
                return done;
            }

            if (attemptNumber < MaxReceiptAttempts)
            {
                await Task.Delay(ReceiptBackoff, timeProvider, cancellationToken);
            }
        }

        logger.LogError("Pushover Receipt send failed for Task {TaskId} after {Attempts} attempts", receipt.TaskId, MaxReceiptAttempts);
        return false;
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

    /// <summary>The two static secrets, once checked — so <see cref="SendAsync"/> holds two
    /// non-nullable strings and needs no null-forgiving operator to use them.</summary>
    private sealed record PushoverCredentials(string Token, string UserKey);

    /// <summary>
    /// Not an attempt at all — a missing Token/UserKey never reaches the network, so it can't be
    /// Accepted, Refused or Rejected. Checked before either caller's loop starts, so it never
    /// consumes one of the Receipt's three attempts. Returns the credentials rather than a bool
    /// so the check and the use stay in one place: a guard hoisted away from the code it protects
    /// is exactly what turns a <c>!</c> into an unjustifiable one.
    /// </summary>
    private PushoverCredentials? Credentials()
    {
        var pushoverOptions = options.Value;
        if (pushoverOptions.IsConfigured)
        {
            // IsConfigured is precisely "neither is null or whitespace", checked one line above.
            return new PushoverCredentials(pushoverOptions.Token!, pushoverOptions.UserKey!);
        }

        logger.LogWarning("Pushover is not configured (missing Token or UserKey); skipping send");
        return null;
    }

    /// <param name="attemptToken">Cancels this one attempt — for a Receipt, the caller's token
    /// linked with that attempt's own timeout.</param>
    /// <param name="callerToken">The caller's token alone. The timeout guard below must test
    /// this one: <paramref name="attemptToken"/> is cancelled by the timeout itself, so guarding
    /// on it would turn every timeout into a propagating cancellation.</param>
    private async Task<PushoverAttempt> SendAsync(
        PushoverCredentials credentials,
        string title,
        string message,
        Uri url,
        int priority,
        CancellationToken attemptToken,
        CancellationToken callerToken)
    {
        try
        {
            var httpClient = httpClientFactory.CreateClient(HttpClientName);
            using var response = await httpClient.PostAsync(
                MessagesUrl,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["token"] = credentials.Token,
                    ["user"] = credentials.UserKey,
                    ["title"] = title,
                    ["message"] = message,
                    ["url"] = url.ToString(),
                    ["priority"] = priority.ToString(),
                }),
                attemptToken);

            if (response.IsSuccessStatusCode)
            {
                return new PushoverAccepted();
            }

            logger.LogError("Pushover send failed with status {StatusCode}", response.StatusCode);

            var statusCode = (int)response.StatusCode;
            return statusCode is >= 400 and < 500
                ? new PushoverRejected(statusCode)
                : new PushoverRefused($"HTTP {statusCode}");
        }
        // A TaskCanceledException raised because the *caller* cancelled is a genuine cancellation,
        // not a send failure — the `when` guard excludes that case, so it propagates instead of
        // being swallowed into a retry. What reaches this catch is this attempt's own timeout
        // (ReceiptAttemptTimeout), which cancels attemptToken but never callerToken.
        // OperationCanceledException, not just its TaskCanceledException subclass: which of the
        // two surfaces depends on where the token was observed, and both mean the same thing here.
        catch (OperationCanceledException ex) when (!callerToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Pushover send timed out");
            return new PushoverRefused("timeout");
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Pushover send failed");
            return new PushoverRefused(ex.Message);
        }
    }
}
