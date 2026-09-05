using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TaskGuide.Infrastructure.Storage;
using Xunit;

namespace TaskGuide.Api.Tests;

/// <summary>
/// ADR-0009: a conscious startup refusal must stop the host before any endpoint or the tick loop
/// can start — <c>Program.cs</c> awaits <see cref="StartupBootstrap.BootstrapAndOpenStoreAsync"/>
/// before <c>builder.Build()</c>, so a refusal throws out of top-level statement execution itself,
/// not out of a request handler (`tests/TEST-INVENTORY.md`, `TaskGuide.Api.Tests` section, #78).
/// </summary>
public sealed class StartupRefusalTests : IDisposable
{
    private const string DataDirEnvVar = "Storage__DataDir";

    private readonly string _dataDir = Directory.CreateTempSubdirectory("taskguide-startup-refusal-tests-").FullName;

    public void Dispose() => Directory.Delete(_dataDir, recursive: true);

    [Fact]
    public void Host_creation_refuses_a_future_version_store_before_any_endpoint_or_the_tick_loop_can_start()
    {
        File.WriteAllText(
            Path.Combine(_dataDir, "manifest.json"),
            JsonSerializer.Serialize(new { version = ManifestCodec.CurrentVersion + 1 }));

        Environment.SetEnvironmentVariable(DataDirEnvVar, _dataDir);
        try
        {
            // Host creation itself is deferred to CreateClient() (WebApplicationFactory only
            // builds the host lazily), which is where the refusal has to surface.
            using var factory = new WebApplicationFactory<Program>();
            var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

            var innermost = ex;
            while (innermost.InnerException is not null) innermost = innermost.InnerException;

            var versionAhead = Assert.IsType<StoreVersionAheadException>(innermost);
            Assert.Equal(ManifestCodec.CurrentVersion + 1, versionAhead.StoredVersion);
            Assert.Equal(ManifestCodec.CurrentVersion, versionAhead.CurrentVersion);
        }
        finally
        {
            Environment.SetEnvironmentVariable(DataDirEnvVar, null);
        }
    }
}
