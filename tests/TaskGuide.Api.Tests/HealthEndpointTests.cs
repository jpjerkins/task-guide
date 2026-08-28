using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace TaskGuide.Api.Tests;

/// <summary>
/// `/health` reports <c>{ ok, lastTick, storage, uptime }</c> and is reachable without
/// traversing <c>/api</c> (`tests/TEST-INVENTORY.md`, `TaskGuide.Api.Tests` section).
/// </summary>
public sealed class HealthEndpointTests : IDisposable
{
    private const string DataDirEnvVar = "Storage__DataDir";

    private readonly string _dataDir = Directory.CreateTempSubdirectory("taskguide-health-endpoint-tests-").FullName;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public HealthEndpointTests()
    {
        Environment.SetEnvironmentVariable(DataDirEnvVar, _dataDir);
        try
        {
            _factory = new WebApplicationFactory<Program>();
            _client = _factory.CreateClient();
        }
        finally
        {
            Environment.SetEnvironmentVariable(DataDirEnvVar, null);
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        Directory.Delete(_dataDir, recursive: true);
    }

    [Fact]
    public async Task Health_is_reachable_at_the_root_without_the_api_prefix()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("ok", out _));
        Assert.True(body.TryGetProperty("lastTick", out _));
        Assert.True(body.TryGetProperty("storage", out _));
        Assert.True(body.TryGetProperty("uptime", out _));
    }

    // No "GET /api/health returns non-200" test: `/api/health` genuinely isn't mapped, but
    // MapFallbackToFile("index.html") correctly answers 200 for any unmatched route (the SPA
    // owns client-side routing) — including that one. A 200 there is the SPA fallback working
    // as designed, not a leak of the health route into /api. The requirement this section is
    // actually about — "/health is reachable without traversing /api" — is proven by the test
    // above: GET /health returns the real health payload at the root.
}
