using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace TaskGuide.Api.Tests;

public sealed class ReminderEndpointsTests : IDisposable
{
    private const string DataDirEnvVar = "Storage__DataDir";
    private readonly string _dataDir = Directory.CreateTempSubdirectory("taskguide-reminder-api-tests-").FullName;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ReminderEndpointsTests()
    {
        var fires = Path.Combine(_dataDir, "fires");
        Directory.CreateDirectory(fires);
        File.WriteAllText(Path.Combine(fires, "2026-09-05.json"), """
            [{ "windowId": "w_late", "kind": "window", "windowName": "Late",
               "windowStart": "23:45", "windowEnd": "23:55", "dueAt": null,
               "firedAt": "2026-09-06T04:45:00Z", "matched": 1, "carried": null }]
            """);

        Environment.SetEnvironmentVariable(DataDirEnvVar, _dataDir);
        try
        {
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(new FixedTimeProvider(
                        new DateTimeOffset(2026, 9, 6, 4, 56, 0, TimeSpan.Zero)));
                }));
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
    public async Task POST_api_reminders_date_windowId_snooze_rejects_a_re_fire_crossing_the_day_boundary_with_the_same_line_the_disabled_control_shows()
    {
        var response = await _client.PostAsync("/api/reminders/2026-09-05/w_late/snooze", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("Snooze ends at midnight", body?.Error);
    }

    private sealed record ErrorResponse(string Error);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
