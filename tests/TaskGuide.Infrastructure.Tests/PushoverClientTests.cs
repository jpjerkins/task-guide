using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Notifications;
using TaskGuide.Infrastructure.Pushover;
using Xunit;

namespace TaskGuide.Infrastructure.Tests;

/// <summary>
/// Constraints from #51/#3/#85/#118: two static secrets from configuration (never hardcoded),
/// never priority 1, a missing token logs and no-ops rather than crashing the tick loop, and the
/// Receipt retry contract from <c>CONTEXT.md</c> § Receipt.
/// </summary>
public sealed class PushoverClientTests
{
    /// <summary>Captures every request it's given and answers with a scripted sequence of responses.</summary>
    private sealed class CapturingHandler(params Func<HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public CapturingHandler() : this([Accepted])
        {
        }

        public static HttpResponseMessage Accepted() => new(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"status":1,"request":"abc"}"""),
        };

        public static Func<HttpResponseMessage> Status(HttpStatusCode code) =>
            () => new HttpResponseMessage(code) { Content = new StringContent("") };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            // Clamp to the last scripted response so a caller that scripts fewer responses than
            // attempts still gets a defined (repeated) answer rather than an index-out-of-range.
            var index = Math.Min(Requests.Count - 1, responses.Length - 1);
            return Task.FromResult(responses[index]());
        }
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    /// <summary>Records every log entry instead of writing anywhere, so a test can assert one happened.</summary>
    private sealed class CapturingLogger : ILogger<PushoverClient>
    {
        public List<LogLevel> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add(logLevel);
    }

    private static Receipt SampleReceipt() => new(
        new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5FAV"),
        "Fix the shelf bracket",
        "30",
        new Uri("https://task-guide.example.ts.net/tasks/t_01ARZ3NDEKTSV4RRFFQ69G5FAV"));

    private static Reminder SampleReminder() => new(
        "Fix the shelf bracket",
        "30",
        [],
        0,
        [],
        new FooterCounts(0, 0, 0),
        [],
        new Uri("https://task-guide.example.ts.net/tasks/t_01ARZ3NDEKTSV4RRFFQ69G5FAV"),
        DateTimeOffset.UtcNow.AddHours(1));

    private static PushoverClient MakeClient(HttpMessageHandler handler, ILogger<PushoverClient>? logger = null) =>
        new(
            new StubHttpClientFactory(new HttpClient(handler)),
            Options.Create(new PushoverOptions { Token = "token123", UserKey = "user123" }),
            logger ?? NullLogger<PushoverClient>.Instance);

    [Fact]
    public async Task A_send_never_carries_priority_1()
    {
        var handler = new CapturingHandler();
        var client = MakeClient(handler);

        await client.SendReceiptAsync(SampleReceipt(), CancellationToken.None);

        var sent = Assert.Single(handler.Requests);
        var form = await sent.Content!.ReadAsStringAsync();
        Assert.DoesNotContain("priority=1&", form);
        Assert.DoesNotContain("priority=1", form.Split('&').Select(Uri.UnescapeDataString));
    }

    [Fact]
    public async Task A_missing_token_no_ops_without_making_an_http_call()
    {
        var handler = new CapturingHandler();
        var options = Options.Create(new PushoverOptions { Token = null, UserKey = null });
        var client = new PushoverClient(new StubHttpClientFactory(new HttpClient(handler)), options, NullLogger<PushoverClient>.Instance);

        await client.SendReceiptAsync(SampleReceipt(), CancellationToken.None);

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Every_failed_attempt_is_logged()
    {
        var handler = new CapturingHandler(CapturingHandler.Status(HttpStatusCode.InternalServerError));
        var logger = new CapturingLogger();
        var client = MakeClient(handler, logger);

        await client.SendReminderAsync(SampleReminder(), CancellationToken.None);

        Assert.Contains(LogLevel.Error, logger.Entries);
    }

    [Fact]
    public async Task A_reminder_is_never_retried_by_the_adapter()
    {
        var handler = new CapturingHandler(
            CapturingHandler.Status(HttpStatusCode.InternalServerError),
            CapturingHandler.Status(HttpStatusCode.InternalServerError),
            CapturingHandler.Status(HttpStatusCode.InternalServerError));
        var client = MakeClient(handler);

        var sent = await client.SendReminderAsync(SampleReminder(), CancellationToken.None);

        Assert.False(sent);
        Assert.Single(handler.Requests);
    }
}
