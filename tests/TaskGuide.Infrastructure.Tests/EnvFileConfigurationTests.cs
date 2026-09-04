using Microsoft.Extensions.Configuration;
using TaskGuide.Infrastructure.Configuration;
using Xunit;

namespace TaskGuide.Infrastructure.Tests;

/// <summary>
/// vault-t2 serves each service's envfile over FUSE at /run/vault-t2-fs/envfiles/&lt;service&gt;,
/// readable ONLY by the service's own UID — it denies even root. That rules out compose's
/// `env_file:`, which the docker client resolves at deploy time as root (#51). So the process
/// reads its own envfile at startup instead, as 50013, the way gmail-mcp does.
/// </summary>
public sealed class EnvFileConfigurationTests
{
    private static string WriteTemp(string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"envfile-{Guid.NewGuid():N}");
        File.WriteAllText(path, contents);
        return path;
    }

    [Fact]
    public void Double_underscore_keys_bind_to_nested_configuration()
    {
        // The envfile uses .NET's nested-key separator, so no code maps names by hand.
        var path = WriteTemp("Pushover__Token=tok123\nPushover__UserKey=usr456\n");
        try
        {
            var config = new ConfigurationBuilder().AddEnvFile(path, optional: false).Build();

            Assert.Equal("tok123", config["Pushover:Token"]);
            Assert.Equal("usr456", config["Pushover:UserKey"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_missing_envfile_is_tolerated_when_optional()
    {
        // Local dev and the test suite have no FUSE mount; absence must not crash startup.
        var config = new ConfigurationBuilder()
            .AddEnvFile("/nonexistent/envfiles/task-guide", optional: true)
            .Build();

        Assert.Null(config["Pushover:Token"]);
    }

    [Fact]
    public void A_missing_envfile_throws_when_required()
    {
        Assert.Throws<FileNotFoundException>(() =>
            new ConfigurationBuilder()
                .AddEnvFile("/nonexistent/envfiles/task-guide", optional: false)
                .Build());
    }

    [Fact]
    public void Blank_lines_and_comments_are_ignored()
    {
        var path = WriteTemp("# vault-t2 generated\n\nPushover__Token=tok123\n\n# trailing\n");
        try
        {
            var config = new ConfigurationBuilder().AddEnvFile(path, optional: false).Build();

            Assert.Equal("tok123", config["Pushover:Token"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_value_containing_equals_signs_survives_intact()
    {
        // Pushover keys are alphanumeric, but base64-ish secrets elsewhere are not — split once.
        var path = WriteTemp("Pushover__Token=a=b=c\n");
        try
        {
            var config = new ConfigurationBuilder().AddEnvFile(path, optional: false).Build();

            Assert.Equal("a=b=c", config["Pushover:Token"]);
        }
        finally { File.Delete(path); }
    }
}
