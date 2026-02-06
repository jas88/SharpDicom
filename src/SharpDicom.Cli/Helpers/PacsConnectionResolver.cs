using System;
using SharpDicom.Cli.Configuration;

namespace SharpDicom.Cli.Helpers;

/// <summary>
/// Resolves PACS connection parameters from CLI flags, connection string, or config profile.
/// Priority: explicit flags > connection string > profile > default profile.
/// </summary>
internal static class PacsConnectionResolver
{
    /// <summary>
    /// Resolves PACS connection parameters.
    /// </summary>
    /// <param name="host">Explicit host from CLI flag.</param>
    /// <param name="port">Explicit port from CLI flag.</param>
    /// <param name="calledAe">Explicit Called AE from CLI flag.</param>
    /// <param name="callingAe">Explicit Calling AE from CLI flag.</param>
    /// <param name="connectionString">Connection string (pacs://AET@host:port).</param>
    /// <param name="profileName">Named profile from config.</param>
    /// <returns>Resolved (host, port, calledAe, callingAe) or null if nothing specified.</returns>
    public static (string host, int port, string calledAe, string callingAe)? Resolve(
        string? host,
        int port,
        string? calledAe,
        string? callingAe,
        string? connectionString,
        string? profileName)
    {
        // 1. Explicit flags: if host and calledAe are both specified, use them
        if (!string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(calledAe))
        {
            return (host!, port, calledAe!, callingAe ?? "SHARPDCM");
        }

        // 2. Connection string
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            if (ConnectionStringParser.TryParse(connectionString!, out var csHost, out var csPort, out var csCalled))
            {
                return (csHost!, csPort, csCalled!, callingAe ?? "SHARPDCM");
            }

            Console.Error.WriteLine($"Warning: Failed to parse connection string '{connectionString}'.");
        }

        // 3. Named profile from config
        if (!string.IsNullOrWhiteSpace(profileName))
        {
            var config = ConfigLoader.Load();
            if (config.Profiles.TryGetValue(profileName!, out var profile))
            {
                return (
                    profile.Host,
                    profile.Port,
                    profile.CalledAE,
                    callingAe ?? profile.CallingAE ?? "SHARPDCM");
            }

            Console.Error.WriteLine($"Warning: Profile '{profileName}' not found in config.");
        }

        // 4. Default profile from config
        {
            var config = ConfigLoader.Load();
            if (!string.IsNullOrWhiteSpace(config.DefaultProfile) &&
                config.Profiles.TryGetValue(config.DefaultProfile!, out var defaultProfile))
            {
                return (
                    defaultProfile.Host,
                    defaultProfile.Port,
                    defaultProfile.CalledAE,
                    callingAe ?? defaultProfile.CallingAE ?? "SHARPDCM");
            }
        }

        return null;
    }
}
