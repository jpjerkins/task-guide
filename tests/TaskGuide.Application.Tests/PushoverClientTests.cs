using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Notifications;
using TaskGuide.Infrastructure.Pushover;
using Xunit;

namespace TaskGuide.Application.Tests;

/// <summary>
/// Constraints from #51/#3: two static secrets from configuration (never hardcoded), never
/// priority 1, and a missing token logs and no-ops rather than crashing the tick loop.
/// </summary>
public sealed class PushoverClientTests
{
    /// <summary>Captures every request it's given instead of hitting the network.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"status":1,"request":"abc"}"""),
            });
        }
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private static Receipt SampleReceipt() => new(
        new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5FAV"),
        "Fix the shelf bracket",
        "30",
        new Uri("https://task-guide.example.ts.net/tasks/t_01ARZ3NDEKTSV4RRFFQ69G5FAV"));

    [Fact]
    public async Task A_send_never_carries_priority_1()
    {
        var handler = new CapturingHandler();
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new PushoverOptions { Token = "token123", UserKey = "user123" });
        var client = new PushoverClient(new StubHttpClientFactory(httpClient), options, NullLogger<PushoverClient>.Instance);

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
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new PushoverOptions { Token = null, UserKey = null });
        var client = new PushoverClient(new StubHttpClientFactory(httpClient), options, NullLogger<PushoverClient>.Instance);

        await client.SendReceiptAsync(SampleReceipt(), CancellationToken.None);

        Assert.Empty(handler.Requests);
    }
}
