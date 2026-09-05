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

        public static Func<HttpResponseMessage> Throws(Exception ex) => () => throw ex;


        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // A real handler observes its token; this one must too, or a test can't tell an
            // attempt that was cut short by its own timeout from one that was answered.
            cancellationToken.ThrowIfCancellationRequested();
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
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    /// <summary>
    /// A <see cref="TimeProvider"/> whose timers fire synchronously and immediately when their
    /// due time is at or below <paramref name="fireUpTo"/> (default 1 second), so a test
    /// exercising the Receipt's backoff (500ms) never actually sleeps, while the Receipt's
    /// per-attempt timeout (3s, fix 1 of #85's review) does NOT fire on its own — a plain Receipt
    /// round-trip test would otherwise have every attempt preempted by its own timeout before the
    /// scripted HTTP response is ever read. A test exercising the timeout itself passes a larger
    /// <paramref name="fireUpTo"/>. Every requested delay lands in <see cref="RequestedDelays"/>
    /// regardless of whether it fired; only the ones that actually fired land in
    /// <see cref="FiredDelays"/>.
    /// </summary>
    private sealed class ImmediateTimeProvider(TimeSpan? fireUpTo = null) : TimeProvider
    {
        private readonly TimeSpan _fireUpTo = fireUpTo ?? TimeSpan.FromSeconds(1);

        public List<TimeSpan> RequestedDelays { get; } = [];

        public List<TimeSpan> FiredDelays { get; } = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            RequestedDelays.Add(dueTime);
            if (dueTime <= _fireUpTo)
            {
                FiredDelays.Add(dueTime);
                callback(state);
            }

            return new NoOpTimer();
        }

        private sealed class NoOpTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
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

    private static PushoverClient MakeClient(HttpMessageHandler handler, TimeProvider? timeProvider = null, ILogger<PushoverClient>? logger = null) =>
        new(
            new StubHttpClientFactory(new HttpClient(handler)),
            Options.Create(new PushoverOptions { Token = "token123", UserKey = "user123" }),
            logger ?? NullLogger<PushoverClient>.Instance,
            timeProvider ?? new ImmediateTimeProvider());

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
        var client = new PushoverClient(
            new StubHttpClientFactory(new HttpClient(handler)), options, NullLogger<PushoverClient>.Instance, new ImmediateTimeProvider());

        await client.SendReceiptAsync(SampleReceipt(), CancellationToken.None);

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Every_failed_attempt_is_logged()
    {
        var handler = new CapturingHandler(CapturingHandler.Status(HttpStatusCode.InternalServerError));
        var logger = new CapturingLogger();
        var client = MakeClient(handler, logger: logger);

        await client.SendReminderAsync(SampleReminder(), CancellationToken.None);

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error);
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

    [Fact]
    public async Task A_receipt_is_retried_up_to_three_times_while_pushover_has_not_accepted()
    {
        var handler = new CapturingHandler(
            CapturingHandler.Status(HttpStatusCode.InternalServerError),
            CapturingHandler.Status(HttpStatusCode.InternalServerError),
            CapturingHandler.Status(HttpStatusCode.InternalServerError));
        var client = MakeClient(handler);

        var sent = await client.SendReceiptAsync(SampleReceipt(), CancellationToken.None);

        Assert.False(sent);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task A_receipt_accepted_on_the_first_attempt_is_not_retried()
    {
        var handler = new CapturingHandler(CapturingHandler.Accepted);
        var timeProvider = new ImmediateTimeProvider();
        var client = MakeClient(handler, timeProvider);

        var sent = await client.SendReceiptAsync(SampleReceipt(), CancellationToken.None);

        Assert.True(sent);
        Assert.Single(handler.Requests);
        Assert.Empty(timeProvider.FiredDelays);
    }

    [Fact]
    public async Task A_4xx_is_never_retried()
    {
        var handler = new CapturingHandler(
            CapturingHandler.Status(HttpStatusCode.BadRequest),
            CapturingHandler.Status(HttpStatusCode.InternalServerError),
            CapturingHandler.Status(HttpStatusCode.InternalServerError));
        var client = MakeClient(handler);

        var sent = await client.SendReceiptAsync(SampleReceipt(), CancellationToken.None);

        Assert.False(sent);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_short_backoff_separates_the_attempts()
    {
        var handler = new CapturingHandler(
            CapturingHandler.Status(HttpStatusCode.InternalServerError),
            CapturingHandler.Status(HttpStatusCode.InternalServerError),
            CapturingHandler.Status(HttpStatusCode.InternalServerError));
        var timeProvider = new ImmediateTimeProvider();
        var client = MakeClient(handler, timeProvider);

        await client.SendReceiptAsync(SampleReceipt(), CancellationToken.None);

        // 3 attempts, backoff between each: 2 elapsed delays, one per gap. (RequestedDelays also
        // holds each attempt's 3s timeout, armed but never reached.)
        Assert.Equal(2, timeProvider.FiredDelays.Count);
        Assert.All(timeProvider.FiredDelays, delay => Assert.True(delay > TimeSpan.Zero));
    }

    [Fact]
    public async Task A_callers_own_cancellation_is_not_swallowed_into_a_retry()
    {
        using var cts = new CancellationTokenSource();
        var handler = new CapturingHandler(CapturingHandler.Throws(new TaskCanceledException("caller cancelled", null, cts.Token)));
        var client = MakeClient(handler);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SendReceiptAsync(SampleReceipt(), cts.Token));
    }

    [Fact]
    public async Task A_receipt_attempt_that_exceeds_its_budget_is_refused_and_retried()
    {
        // fireUpTo is raised past the 3s budget so the fake clock actually fires each attempt's
        // timeout. The handler would answer 200 if it were ever reached — so a pass here means
        // the budget, not the response, decided the outcome.
        var handler = new CapturingHandler();
        var timeProvider = new ImmediateTimeProvider(TimeSpan.FromSeconds(5));
        var client = MakeClient(handler, timeProvider);

        var sent = await client.SendReceiptAsync(SampleReceipt(), CancellationToken.None);

        Assert.False(sent);
        Assert.Empty(handler.Requests);
        Assert.Equal(3, timeProvider.FiredDelays.Count(d => d == TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task A_4xx_names_the_task_in_the_log()
    {
        var handler = new CapturingHandler(CapturingHandler.Status(HttpStatusCode.BadRequest));
        var logger = new CapturingLogger();
        var client = MakeClient(handler, logger: logger);

        await client.SendReceiptAsync(SampleReceipt(), CancellationToken.None);

        Assert.Contains(
            logger.Entries,
            e => e.Level == LogLevel.Error && e.Message.Contains(SampleReceipt().TaskId.Value));
    }
}
