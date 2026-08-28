using Microsoft.Extensions.Configuration;

namespace TaskGuide.Infrastructure.Configuration;

/// <summary>
/// Reads a `KEY=VALUE` env file into configuration from inside the running process.
/// </summary>
/// <remarks>
/// Exists because compose's `env_file:` cannot reach vault-t2. The docker client resolves
/// `env_file` at deploy time, running as root — and vault-t2's FUSE mount serves each service's
/// envfile only to that service's declared UID, denying root. Reading it here, as 50013, is the
/// pattern gmail-mcp already uses on pi5 (#51).
///
/// Keys use `__`, .NET's nested-key separator, so vault-t2's `envfiles.yaml` names map straight
/// onto `Pushover:Token` with no hand-written translation.
/// </remarks>
public static class EnvFileConfigurationExtensions
{
    /// <param name="optional">
    /// True outside the container, where no FUSE mount exists. A missing file must not stop
    /// startup — PushoverClient already logs and no-ops when the token is absent.
    /// </param>
    public static IConfigurationBuilder AddEnvFile(this IConfigurationBuilder builder, string path, bool optional)
    {
        if (!File.Exists(path))
        {
            if (optional) return builder;
            throw new FileNotFoundException($"Required env file not found: {path}", path);
        }

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            // Split once: a value may legitimately contain '='.
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;

            var key = line[..separator].Trim().Replace("__", ConfigurationPath.KeyDelimiter);
            values[key] = line[(separator + 1)..].Trim();
        }

        return builder.AddInMemoryCollection(values);
    }
}
